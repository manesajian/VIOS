import errno
import os
import queue
import random
import sys
import struct
import threading
import time

import imp
vioslib = imp.load_source('vioslib', 'vioslib.py')

def get_subdirectories(path):
    return [name for name in os.listdir(path)
            if os.path.isdir(os.path.join(path, name.lower())) and name != '__pycache__']

class AudiList(vioslib.VIOSApp):
    def __init__(self, queueHandler):
        vioslib.VIOSApp.__init__(self, queueHandler)

        self.name = 'AudiList'

    def cleanup(self):
        vioslib.VIOSApp.cleanup(self)  

    def run(self, queueHandler):
        vioslib.VIOSApp.run(self)

        vioslib.log_msg('Registered AudiList as {0}.'.format(self.instanceId))

        self.main()

        self.cleanup()

    def main(self):
        self.synthesize('Welcome to AudiList.')

        currentNode = os.path.dirname(os.path.realpath(__file__))
        while self.interrupted == False:
            # start with always-valid choices
            choices = ['root', 'parent',
                       'create', 'delete',
                       'record', 'play', 'clear',
                       'list', 'exit', 'break']

            # get list of child nodes and extend choices
            child_nodes = get_subdirectories(currentNode)
            choices.extend(child_nodes)

            choice = self.grammar_prompt_and_read(choices, 'Choose command, List, or Exit.')

            if choice == 'root':
                self.synthesize('Returning to root.')
                currentNode = os.path.dirname(os.path.realpath(__file__))
            elif choice == 'parent':
                self.synthesize('Returning to parent.')
                head, tail = os.path.split(currentNode)
                currentNode = head
            elif choice == 'create':
                node_name = self.grammar_prompt_and_read([], 'Choose node name.')

                confirm = ''
                while confirm != 'yes' and confirm != 'break':
                    confirm = self.grammar_prompt_and_read(['no', 'yes', 'break'],
                                                           'Confirm {0}'.format(node_name))

                    if confirm == 'no':
                        node_name = self.grammar_prompt_and_read([], 'Choose node name.')

                if confirm == 'yes':
                    try: 
                        os.makedirs('{0}\\{1}'.format(currentNode, node_name))
                    except OSError:
                        if not os.path.isdir('{0}\\{1}'.format(currentNode, node_name)):
                            raise

                        vioslib.log_msg('Error during os.makedirs().')

                    self.synthesize('Created {0}'.format(node_name));
            elif choice == 'delete':
                node_name = self.grammar_prompt_and_read([], 'Choose node name.')
                confirm = ''
                while confirm != 'yes' and confirm != 'break':
                    confirm = self.grammar_prompt_and_read(['no', 'yes', 'break'],
                                                           'Confirm {0}'.format(node_name))

                    if confirm == 'no':
                        node_name = self.grammar_prompt_and_read([], 'Choose node name.')

                if confirm == 'yes':
                    try:
                        os.rmdir('{0}\\{1}'.format(currentNode, node_name))
                        self.synthesize('Deleted {0}'.format(node_name))
                    except:
                        self.synthesize('Error deleting node. Clear node first.')
            elif choice == 'record':
                self.send_command('record', '{0}\\audiofile.wav'.format(currentNode))

                # request notification of recording completion
                self.send_command('recordDone')

                # wait for feedback
                result = self.read().args

                if result != 'record done':
                    raise Exception('Unexpected result ({0}) returned from recording.'.format(result))

                self.synthesize('Finished recording.')
            elif choice == 'play':
                self.send_command('play', '{0}\\audiofile.wav,1.0'.format(currentNode))

                # request notification of audio completion
                self.send_command('playerDone')

                # wait for audio completion or user wanting to cancel
                result = self.read().args
                while result != 'player done' and result != 'break' and result != 'exit':
                    result = self.read().args
            elif choice == 'clear':
                confirm = ''

                confirm = self.grammar_prompt_and_read(['no', 'yes', 'break'], 'Confirm clear.')

                if confirm == 'yes':
                    try:
                        os.remove('{0}\\audiofile.wav'.format(currentNode))
                        self.synthesize('Cleared audio.')
                    except:
                        self.synthesize('Could not clear.')
            elif any(choice == node for node in child_nodes):
                self.synthesize('Going to node {0}.'.format(choice))
                currentNode += '\\' + choice
            elif choice == 'list':
                node_str = ', '.join(str(x) for x in child_nodes)
                if node_str != '':
                    node_str += ', '
                self.synthesize(node_str + 'Root, Parent, Create, Delete, Record, Play, Clear, List, Exit.')    
                
                list_str = ''
                for node in child_nodes:
                    if node != '__pycache__':
                        list_str += node + ','
                list_str = list_str.rstrip(',')                
# TODO: fix list
                self.synthesize(list_str)
            elif choice == 'exit' or choice == 'break':
                self.synthesize('Leaving AudiList.')
                self.background()

        vioslib.log_msg('Exiting AudiList.')
