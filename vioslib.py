import collections
import datetime
import os
import random
import sys
import struct
import threading
import time

def log_msg(msg):
    dtstr = str(datetime.datetime.now()).split('.')[0]
    print('{0}: {1}'.format(dtstr, msg))

class VIOSApp(threading.Thread):
    def __init__(self, _queueHandler):
        threading.Thread.__init__(self)
        
        self.queueHandler = _queueHandler

        self.name = 'Default'
        
        # indicates whether initialization has finished
        self.initialized = False

        # indicates whether this app is currently foregrounded
        self.active = False

        # this flag is used to break app out of any loops on system shutdown
        self.interrupted = False

        # indicates if app has finished
        self.exited = False

        # current grammar
        self.choices = []

        # used to disable/reenable grammar
        self.disabledChoices = []
        self.disabledGrammar = False

        # used to re-prompt when app is foregrounded
        self.lastSynthesis = ''

        # used to protect changes to the app's current grammar
        self.grammarLock = threading.Lock()

    def cleanup(self):
        self.initialized = False
        self.active = False

        self.choices = []
        self.disabledChoices = []
        self.disabledGrammar = False
        self.lastSynthesis = ''
        
        self.exited = True

        # causes grammar-matching to be reset for this instance
        self.queueHandler.grammarMapper.set_grammar(self.instanceId, []) 

# TODO: need to do something about QueueHandler registration here?

    def run(self):
        # get unique instance id
        self.instanceId = self.queueHandler.get_instance_id()

        # register instance with QueueHandler
        self.queueHandler.register_instance(self.instanceId)

        # this also happens in foreground(), would be nice to reduce to 1 place
        self.queueHandler.grammarMapper.activeApp = self

        self.initialized = True
        self.active = True
        self.exited = False
        self.choices = []
        self.lastSynthesis = ''

    def background(self):
        self.queueHandler.grammarMapper.activeApp = None
        self.active = False

    def foreground(self):
        self.active = True
        self.queueHandler.grammarMapper.activeApp = self
        
        # re-activate grammar
        self.set_choices(self.choices)

        # re-synthesize last output
        if self.lastSynthesis != '':
            self.synthesize(self.lastSynthesis)

    def disable_grammar(self):
        self.disabledChoices = self.choices
        self.disabledGrammar = True

        # causes grammar-matching to be reset for this instance
        self.set_choices([])
            
    def reenable_grammar(self):
        # causes grammar-matching to be re-enabled for this instance
        self.set_choices(self.disabledChoices)
        
        self.disabledChoices = []
        self.disabledGrammar = False
            
    def synthesize(self, text):
        if self.initialized == False:
            return

        # remember text in case it must be re-synthesized when app is foregrounded
        self.lastSynthesis = text

        # if app is backgrounded, don't actually synthesize anything
        if self.active == False:
            # limit a backgrounded app's synthesizes to one per second
            time.sleep(1)
            return
        
        # start by requesting notification of synthesization availability
        command = self.send_command('synthesisDone')

        # block for confirmation of availability
        self.read(messageId = command.messageId)

        # send synthesis command
        self.queueHandler.write(Message(self.instanceId,
                                        'speechSynth',
                                        self.queueHandler.get_message_id(),
                                        text))

    def trigger_grammar_update(self):
        # sort of a hacky way of not executing if shell hasn't initialized yet
        if '1' not in self.queueHandler.instanceRecvDequeDict:
            return
       
        # get grammar choices through GrammarMapper
        choices = self.queueHandler.grammarMapper.get_grammar()

        setMsg = Message()
        setMsg.instanceId = self.instanceId
        setMsg.type = 'grammarSet'
        setMsg.messageId = self.queueHandler.get_message_id()

        # build grammar choices into comma-delimited string
        choice_str = ''
        for i in range(len(choices)):
            choice_str += choices[i] + ','
        choice_str = choice_str.rstrip(',')

        setMsg.args = choice_str

        # send grammar set
        self.queueHandler.write(setMsg)

        return setMsg
    
    def set_choices(self, newChoices):
        self.grammarLock.acquire()
        try:        
            # remember new grammar
            self.choices = list(newChoices)
            
            self.queueHandler.grammarMapper.set_grammar(self.instanceId, list(newChoices))

            # do an actual update if app is active
            if self.active:
                self.trigger_grammar_update()
        finally:
            self.grammarLock.release()

        # although we're not actually necessarily generating a Message() on a
        #  set_choices anymore, certain app code needs to be able to do blocking
        #  reads using a MessageId tied to both the grammar and ongoing synthesis
        #  (so that it can stop blocking on a grammar choice once synthesis is
        #  complete).
        # it should continue to work for now if we just return a valid message
        #  with a unique MessageId each time set_choices() is called.
        return Message('', '', self.queueHandler.get_message_id(), '')
            
    def send_command(self, type, args = '', messageId = None):
        if self.initialized == False:
            return
        
        command = Message()
        command.instanceId = self.instanceId
        command.type = type

        if messageId == None:
            command.messageId = self.queueHandler.get_message_id()
        else:
            command.messageId = messageId

        command.args = args

        # send command
        self.queueHandler.write(command)

        return command

    def read(self, messageId = None, block = True):
        if self.initialized == False:
            return None
        
        result = self.queueHandler.read(self.instanceId, messageId = messageId, block = block)

        if result != None:
            return result.args

        return None

    # sets grammar choices, outputs prompt, and blocks until it can return with input
    # set choices to [] to trigger dictation-mode
    # set choices to None to use existing grammar
    def grammar_prompt_and_read(self, newChoices, prompt):
        if self.initialized == False:
            return None

# previously the grammarset was resulting in a messageId which was then passed
#  to the read() down below ... thereby associating the read with the grammarset.
#  taking that out due to multi-instance handling being moved from .Net-side to
#  just python-side. Not sure if this is going to affect certain blocking call
#  behavior, so just going to test it

        # set instance grammar, if any choices provided
        if newChoices != None:
            self.set_choices(newChoices)

        # start synthesis, if any prompt provided
        if prompt != '':
            self.synthesize(prompt)

        # wait for feedback
        result = self.read()

        if prompt != '':
            self.send_command('break')

        log_msg('grammar_prompt_and_read(): ' + result)
        
        return result

    def start_dictation(self, endDictationToken, prompt):
        if self.initialized == False:
            return None

        if prompt != '':
            self.synthesize(prompt)

        self.send_command('startDictation,' + endDictationToken)

        # get dictation result
        result = self.read()

        log_msg('start_dictation(): ' + result)

        return result

# used to build list of currently valid grammar choices to send to the recognizer
# also can map a match back to the app it belongs to
class GrammarMapper():
    def __init__(self):
        self.instanceDict = {}

        self.instanceLock = threading.Lock()

        self.activeApp = None

    def register_instance(self, instanceId):
        self.instanceDict[instanceId] = []

    def set_grammar(self, instanceId, choices):
        self.instanceDict[instanceId] = choices

    # returns list of current grammar choices from across all apps
    def get_grammar(self):
        # start with shell grammar
        choices = list(self.instanceDict['1'])

        # extend with active app's grammar
        activeAppChoices = None
        if self.activeApp is not None:
            activeAppChoices = list(self.instanceDict[self.activeApp.instanceId])

        # return [] to represent dictation active in app or shell
        if activeAppChoices == [] and self.activeApp.disabledGrammar == False:
            return []
        elif activeAppChoices == None and choices == []:
            return []

        # merge active app and shell grammars
        if activeAppChoices is not None:
            choices += activeAppChoices

        # weed out duplicates between active app and shell
        temp_dict = {}
        new_list = []
        for choice in choices:
            if choice not in temp_dict:
                temp_dict[choice] = choice
                new_list.append(choice)

        return new_list

    # returns the instance id of the app a grammar match belongs to
    def get_instance(self, match):
##        if self.activeAppId is not None:
##            appGrammarList = self.instanceDict[self.activeAppId]
##            if appGrammarList == [] or match in self.instanceDict[self.activeAppId]:
##                return self.activeAppId
##        else:
##            # this is slightly weird since if there is no active app, the only grammar
##            #  matches that should occur would be the shell. In fact, isn't get_instance()
##            #  really only deciding between the active_app and the shell? Couldn't this
##            #  entire function conceivably be reduced to an if-statement? Possibly it
##            #  will eventually work differently and multiple apps can be receiving input,
##            #  but for now it seems simpler.
        for key1, value1 in self.instanceDict.items():
            if match in value1:
                return key1

        return None

    # returns contents of GrammarMapper as a string
    def dump(self):
        dump_str = 'activeAppId={0}\n'.format(self.activeApp.instanceId)
        for key1, value1 in self.instanceDict.items():
            dump_str += '\tappId={0}\n'.format(key1)
            for value2 in value1:
                dump_str += '\t\t{0}\n'.format(value2)

        return dump_str

class Message():
    def __init__(self, _instanceId = None, _type = None, _messageId = None, _args = None):
        self.instanceId = _instanceId
        self.type = _type
        self.messageId = _messageId
        self.args = _args

    def from_str(self, rawStr):
        # verify string format
        if rawStr.startswith('>>') == False or \
           rawStr.endswith('<<') == False:
               raise Exception('Invalid raw string to Message(): ' + rawStr)

        # trim bracketing characters
        trimStr = rawStr.lstrip('>').rstrip('<')

        # split string into fields
        strElems = trimStr.split('|')

        # verify number of fields
        if len(strElems) != 4:
            raise Exception('Invalid number of fields in Message(): ' + rawStr)

        # store fields
        self.instanceId = strElems[0]
        self.type = strElems[1]
        self.messageId = strElems[2]
        self.args = strElems[3]

        return self

    def to_str(self):
        return '>>' + self.instanceId + '|' + \
                      self.type + '|' + \
                      self.messageId + '|' + \
                      self.args + '<<'
                    
class QueueHandler(threading.Thread):
    def __init__(self, _recvPipe, _sendPipe):
        threading.Thread.__init__(self)
        
        self.recvPipe = _recvPipe
        self.sendPipe = _sendPipe

        self.writeLock = threading.Lock()

        self.nextInstanceId = 1
        self.nextMessageId = 1

        self.instanceRecvDequeDict = {}

        self.instanceLock = threading.Lock()

        # initialize GrammarMapper that helps manage grammars across apps
        # this is attached to QueueHandler for convenient app access
        self.grammarMapper = GrammarMapper()

    def get_instance_id(self):
        instanceId = ''
        self.instanceLock.acquire()
        instanceId = str(self.nextInstanceId)
        self.nextInstanceId += 1
        self.instanceLock.release()

        return instanceId

    def get_message_id(self):
        messageId = ''
        self.instanceLock.acquire()
        messageId = str(self.nextMessageId)
        self.nextMessageId += 1
        self.instanceLock.release()

        return messageId

    def register_instance(self, instanceId):
        self.instanceLock.acquire()
        self.instanceRecvDequeDict[instanceId] = collections.deque()
        self.grammarMapper.register_instance(instanceId)
        self.instanceLock.release()

    def run(self):
        # start reader thread
        self.readerThread = threading.Thread(target = self.process_reads)
        self.readerThread.setDaemon(True)
        self.readerThread.start()

    # used to wake up sleeping apps on system shutdown
    # possibly should become part of VIOSApp eventually
    def wakeup(self, instanceId):
        self.instanceRecvDequeDict[instanceId].append(Message(instanceId,
                                                              'grammarMatch',
                                                              self.get_message_id(),
                                                              'wakeup'))

    def process_reads(self):
        while True:
            # deserialize a message from incoming pipe
            message = Message().from_str(pipe_read(self.recvPipe))

            # use GrammarMapper to look up receiving app for grammar matches
            instanceRecvDeque = None
            instanceId = None
            try:
                if message.type == 'grammarMatch':
                    instanceId = self.grammarMapper.get_instance(message.args)
                elif message.type == 'dictationResult':
                    instanceId = self.grammarMapper.activeApp.instanceId
                else:
                    # for all other msgs, rely on message's instance id
                    instanceId = message.instanceId
            except:
                log_msg('Caught exception in QueueHandler while looking up instance: {0}'.format(message.to_str()))

            if instanceId is not None:
                instanceRecvDeque = self.instanceRecvDequeDict[instanceId]

            # GrammarMapper should never not return a valid instance
            if instanceRecvDeque == None:
                log_msg('process_reads(): no instanceRecvDeque found. Dumping grammarMapper:\n{0}'.format(self.grammarMapper.dump()))

            # perform proxy function by placing message on instance's deque
            instanceRecvDeque.append(message)

            log_msg('Message delivered to instance {0}'.format(instanceId))

            # drop oldest message if instance's deque exceeds maximum
            if len(instanceRecvDeque) > 10:
                instanceRecvDeque.popleft()

            # avoid busy loop
            time.sleep(.1)

    # performs a blocking or non-blocking Message object read for a given instance
    def read(self, instanceId, messageId = None, block = True):
# TODO: verify or fix for thread-safety ...
        # reference instance-specific deque
        recvDeque = self.instanceRecvDequeDict[instanceId]

        # Either pull messageId-specific message or just any instance message
        message = None
        if messageId == None:
            try:
                message = recvDeque.pop()
            except:
                # implement blocking message retrieval
                while block:
                    time.sleep(.2)
                    try:
                        message = recvDeque.pop()
                        break
                    except:
                        pass
        else:
            # NOTE: due to a change to handling multiple grammar sets, namely
            #  handling it now on the python-side rather than in .Net, I am
            #  going to have to hack a change in below to not filter based on
            #  MessageId if the message is a grammarMatch. The reason for this
            #  is that the .Net code is no longer remembering the mapping
            #  from a grammar match to a particular app/message. Now only the
            #  python code knows that relationship.
            # Essentially now when an app calls read(messageId), it will always
            #  return if there is any grammar match belonging to the app. So,
            #  this means the same app could not have two reads blocking on
            #  different messageIds. I don't think this currently poses an issue.
           
            # iterate searching for messageId match
            for msg in recvDeque:
                if msg.messageId == messageId or msg.type == 'grammarMatch' or msg.type == 'dictationResult':
                    # pop matching message from middle of deque
                    message = msg
                    recvDeque.remove(msg)
                    break

            if message == None:
                # implement blocking message retrieval
                while block:
                    time.sleep(.2)
                    for msg in recvDeque:
                        if msg.messageId == messageId or msg.type == 'grammarMatch' or msg.type == 'dictationResult':
                            # pop matching message from middle of deque
                            message = msg
                            recvDeque.remove(msg)
                            break

                    # break out of block loop
                    if message != None:
                        break

        return message

    # thread-protected write on shared pipe
    def write(self, message):
        self.writeLock.acquire()
        pipe_write(self.sendPipe, message.to_str())
        self.writeLock.release()

# writes msg to pipe using simple protocol of length followed by msg
def pipe_write(pipe, writeString):
    log_msg('Sending message: ' + writeString)
    
    # write string length followed by string
    pipe.write(struct.pack('I', len(writeString)) + writeString.encode('ascii'))
    pipe.seek(0)

# reads msg from pipe using simple protocol of length followed by msg
def pipe_read(pipe):
    # read length of expected
    readBytes = pipe.read(4)
    
    # seek to beginning of stream
    pipe.seek(0)

    # error check
    bytesRead = len(readBytes)
    if len(readBytes) < 4:
        log_msg('Returned {0} bytes, expecting 4.'.format(len(readBytes)))

        if bytesRead == 0:
            raise NameError('Error on connection.')
    
        return ''

    # convert length
    stringLength = struct.unpack('I', readBytes)[0]

    # read expected number of bytes
    bytesRead = 0
    readBytes = bytes()
    currentReadBytes = []
    while (bytesRead < stringLength):
        currentReadBytes = pipe.read(stringLength - bytesRead)

        # seek to beginning of stream
        pipe.seek(0)

        if len(currentReadBytes) == 0:
            log_msg('0 bytes read error.')
            return ''

        readBytes = readBytes + currentReadBytes

        bytesRead += len(currentReadBytes)
        
    # convert string
    readString = readBytes.decode('ascii')

    log_msg('Read message: ' + readString)

    return readString

# waits for yes/no (or break)
def pipe_wait_for_confirm(queueHandler, command):
    return pipe_wait_for_choice(queueHandler,
                                  'Confirm ' + command + '. Yes or No.',
                                  [ 'yes', 'no' ])

