import os
import queue
import random
import sys
import struct
import threading
import time

import imp

vioslib = imp.load_source('vioslib', 'vioslib.py')

audilist = imp.load_source('audilist', 'AudiList\\audilist.py')
AudiList = audilist.AudiList

audiplay = imp.load_source('audiplay', 'AudiPlay\\audiplay.py')
AudiPlay = audiplay.AudiPlay

class VIOSShell(vioslib.VIOSApp):
    def __init__(self, queueHandler):
        vioslib.VIOSApp.__init__(self, queueHandler)
        
        # the shell must track which app is 'foregrounded'
        self.activeApp = None

    def cleanup(self):
        # causes grammar-matching to be reset for this instance
        self.send_command('clearInstance')

    def run(self):
        vioslib.VIOSApp.run(self)

        vioslib.log_msg('Registered VIOS shell instance as {0}.'.format(self.instanceId))

        self.main()

    def main(self):
        self.synthesize('Welcome to VIOS.')
        
        # create and populate app dictionary
        apps = {}
        apps['audilist'] = AudiList(self.queueHandler)
        apps['audiplay'] = AudiPlay(self.queueHandler)

        # create shell grammar sets
        app_choices = ['audilist', 'audiplay']
        shell_choices = ['active', 'shell', 'monomorphic']
        shell_exit = ['exit']

        def launch_active_app(newApp):
            # launch or foreground app process
            if newApp.initialized == False:
                newApp.start()
            else:
                newApp.foreground()

            # this is now the active app
            self.activeApp = newApp

        while True:
            # detect if the active app is no longer active
            if self.activeApp and self.activeApp.active == False:
                self.activeApp = None

            # if no active app, do blocking read. Otherwise, non-blocking
            choice = ''
            if self.activeApp == None:
                choice = self.grammar_prompt_and_read(app_choices + shell_choices + shell_exit,
                                                      'Choose App, List, or Exit.')
            else:
                choice = self.read(block = False)

            # if no input (from the non-blocking read), sleep and continue
            if choice == None:
                time.sleep(.1)
                continue

            if choice in apps:
                # set active shell grammar to choices valid from within an app
                self.set_choices(shell_choices)

                app = apps[choice]

                # start app on asynchronous thread
                launch_active_app(app)
            elif choice == 'active':
                if self.activeApp == None:
                    self.synthesize('No active apps.')
                else:
                    self.synthesize('Active app is {0}'.format(self.activeApp.name))
            elif choice == 'shell':
                if self.activeApp == None:
                    self.synthesize('Already in shell.')
                else:
                    self.synthesize('Backgrounding app.')
                    self.activeApp.background()
            elif choice == 'monomorphic':
                # disable app grammar, if any
                if self.activeApp:
                    self.activeApp.disable_grammar()

                # remember current shell grammar before changing it
                saved_choices = self.choices
                
                confirm = self.grammar_prompt_and_read(['no', 'yes'],
                                                       'Confirm monomorphic mode.')

                if confirm == 'yes':
                    # block until user leaves monomorphic mode
                    self.grammar_prompt_and_read(['polymorphic'], 'Entering monomorphic mode.')

                    self.synthesize('Returned to polymorphic mode.')

                    # restore shell grammar
                    self.set_choices(saved_choices)

                # restore app grammar, if any
                if self.activeApp:
                  self.activeApp.reenable_grammar()
            elif choice == 'list':
                self.synthesize('List not implemented yet. Need to collapse number sequences somehow.')
            elif choice == 'exit':
                break
            else:
                vioslib.log_msg('Error: shell received unrecognized input: {0}'.format(choice))

        # iterate over apps dictionary and close each app
        vioslib.log_msg('Closing apps ...')
        for appName, app in apps.items():
            if app.initialized:
                vioslib.log_msg('Interrupting {0} ...'.format(appName))
                app.interrupted = True
                vioslib.log_msg('Waking {0} ...'.format(appName))
                self.queueHandler.wakeup(app.instanceId)
                vioslib.log_msg('Joining {0} ...'.format(appName))
                app.join()

        self.synthesize('Goodbye.')

        vioslib.log_msg('Exiting.')

# if running as a script (instead of being a module), call main
if __name__ == "__main__":
    start = time.time()

    # set up connection for VIOS input
    vioslib.log_msg('Connected to named pipe for receiving ...')
    recvPipe = open(r'\\.\pipe\NPToGE', 'r+b', 0)
    
    # set up connection for VIOS output
    vioslib.log_msg('Connected to named pipe for sending ...')
    sendPipe = open(r'\\.\pipe\NPFromGE', 'r+b', 0)

    # start up QueueHandler that helps proxy between audio engine and apps
    queueHandler = vioslib.QueueHandler(recvPipe, sendPipe)
    queueHandler.setDaemon(True)
    queueHandler.start()
    vioslib.log_msg('Started QueueHandler.')

    vioslib.log_msg('Starting shell.')
    VIOSShell(queueHandler).run()

    # cleanup
## hanging after the first close() for some reason
##    recvPipe.close()
##    sendPipe.close()

    vioslib.log_msg('Elapsed Time: %s' % int(time.time() - start))
