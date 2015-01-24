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

def get_subdirectories_lower(path):
    return [name.lower() for name in os.listdir(path)
            if os.path.isdir(os.path.join(path, name.lower())) and name != '__pycache__']

def get_files(path):
    return [path + '\\' + fn for fn in os.listdir(path) if fn.endswith(('.mp3', 'mp4', '.wav'))]

class AudiPlay(vioslib.VIOSApp):
    def __init__(self, queueHandler):
        vioslib.VIOSApp.__init__(self, queueHandler)

        self.name = 'AudiPlay'

    def cleanup(self):
        vioslib.VIOSApp.cleanup(self)   

    def run(self):
        vioslib.VIOSApp.run(self)

        vioslib.log_msg('Registered AudiPlay as {0}.'.format(self.instanceId))

        self.volume = 1.0

        self.main()

        self.cleanup()

    def enumerate_max(self, limit):
        enumeration = []
        for i in range(limit):
            enumeration.append(str(i + 1))

        return enumeration

    def handle_play(self, audio_files):
        if len(audio_files) == 0:
            self.synthesize('No audio files in node.')
            return
        
        # start by initializing grammar to essentially a bogus value
        # what I really want to do here is suspend voice recognition, but
        # don't currently have a way to do that
        self.set_choices(['neverland'])

        playing = True
        paused = False
        file_index = 0

        polymorphic = True
        while self.interrupted == False:
            # set basic starting choices
            if polymorphic:
                basicNav = ['back', 'skip', 'seek', 'volume down', 'volume up', 'previous', 'next', 'pause', 'stop', 'exit', 'break']
            else:
                basicNav = ['polymorphic']

            setChoices = self.set_choices(basicNav)

            # request notification of audio completion using grammar message id
            self.send_command('playerDone', '', setChoices.messageId)

            # wait for feedback
            result = self.read(setChoices.messageId, True)

            if result == 'player done':
                # begin playing audio file
                self.send_command('play', '{0},{1}'.format(audio_files[file_index], self.volume))

                # advance index to next file in loop
                file_index += 1
                if file_index > len(audio_files) - 1:
                    file_index = 0
            elif result == 'back':
                choices = self.enumerate_max(60).append('break')
                choice = self.grammar_prompt_and_read(choices, '')

                if choice != 'break':
                    self.send_command('back', '{0}'.format(choice))
            elif result == 'skip':
                choices = self.enumerate_max(60).append('break')
                choice = self.grammar_prompt_and_read(choices, '')

                if choice != 'break':
                    self.send_command('skip', '{0}'.format(choice))
            elif result == 'seek':
                choices = self.enumerate_max(99).append('break')
                choice = self.grammar_prompt_and_read(choices, '')

                if choice != 'break':
                    self.send_command('seek', '{0}'.format(choice))
            elif result == 'volume down':
                self.volume /= 2.0
                if self.volume < .125:
                    self.volume = .125

                self.send_command('volume', '{0}'.format(self.volume))
            elif result == 'volume up':
                self.volume *= 2.0
                if self.volume > 1.0:
                    self.volume = 1.0

                self.send_command('volume', '{0}'.format(self.volume))
            elif result == 'previous':
                self.send_command('stop')

                paused = False

                file_index -= 2
                if file_index < 0:
                    file_index = len(audio_files) + file_index
            elif result == 'next':
                self.send_command('stop')

                paused = False
            elif result == 'pause':
                self.send_command('pause')

                self.paused = True

                self.synthesize('Paused player.')

                paused_choices = ['play', 'unpause', 'resume', 'break', 'exit']
                choice = self.grammar_prompt_and_read(paused_choices, '')
                
                if choice == 'play' or choice == 'unpause' or choice == 'resume':
                    self.send_command('unpause')
                    self.paused = False
                elif choice == 'exit' or choice == 'break':
                    break              
            elif result == 'stop' or result == 'exit' or result == 'break':
                break
            else:
                vioslib.log_msg('AudiPlay received unrecognized command ({0})'.format(choice))

        self.send_command('stop')

        self.paused = False
        
        self.synthesize('Stopped player.')

    def handle_randomize(self, currentNode):
        audio_files = []
        
        # build randomized list of files
        temp_files = get_files(currentNode)
        while (len(temp_files) > 0):
            audio_files.append(temp_files.pop(random.randrange(0, len(temp_files))))

        self.handle_play(audio_files)

    def main(self):
        self.synthesize('Welcome to Audi Play.')

        currentNode = os.path.dirname(os.path.realpath(__file__))
        while self.interrupted == False:
            choices = ['root', 'parent', 'play', 'randomize', 'select', 'list', 'exit', 'break']

            # get list of child nodes
            child_nodes = get_subdirectories(currentNode)
            child_nodes_lower = get_subdirectories_lower(currentNode)

            # extend choices
            choices.extend(child_nodes)

            choice = self.grammar_prompt_and_read(choices,
                                                  'Choose command, List, or Exit.')

            if choice == 'root':
                self.synthesize('Returning to root.')
                currentNode = os.path.dirname(os.path.realpath(__file__))
            elif choice == 'parent':
                self.synthesize('Returning to parent.')
                head, tail = os.path.split(currentNode)
                currentNode = head
            elif choice == 'play':
                self.handle_play(get_files(currentNode))
            elif choice == 'randomize':
                self.handle_randomize(currentNode)
            elif any(choice == node for node in child_nodes_lower):
                self.synthesize('Going to node {0}.'.format(choice))
                currentNode += '\\' + choice
            elif choice == 'select':
                # build node string with preceding indices
                node_str = ''
                node_choices = []
                node_index = 1
                for node in child_nodes:
                    node_str += '{0} - {1}, '.format(node_index, node)
                    node_choices.append(str(node_index))
                    node_index += 1
                node_str = node_str.rstrip(',')

                if node_str != '':
                    # limit grammar choices to selecting a node or breaking
                    choices = list(node_choices)
                    choices.extend(['break', 'exit'])
                    grammarSet = self.set_choices(choices)

                    # start synthesizing
                    self.synthesize(node_str)

                    # request notification of synthesization availability
                    command = self.send_command('synthesisDone', '', grammarSet.messageId)

                    # block for node selection, break, or synthesis completion
                    result = self.read(grammarSet.messageId, True).args

                    if any(result == node for node in node_choices):
                        self.send_command('break')
                        chosen_node = child_nodes[int(result) - 1]
                        self.synthesize('Going to node {0}.'.format(chosen_node))
                        currentNode += '\\' + chosen_node
                    elif result == 'break' or result == 'exit':
                        self.send_command('break')
                    else:
                        vioslib.log_msg('hmm command == <{0}>'.format(result))
                else:
                    self.synthesize('No children nodes available.')                    
            elif choice == 'list':
                self.synthesize('Root, Parent, Play, Randomize, Select, List, Exit.')              
            elif choice == 'exit' or choice == 'break':
                self.synthesize('Leaving Audi Play.')
                self.background()
            else:
                vioslib.log_msg('AudiPlay: unrecognized grammar match >{0}<'.format(choice))

        vioslib.log_msg('Exiting AudiPlay.')
