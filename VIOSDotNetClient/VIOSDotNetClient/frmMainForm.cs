using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using NAudio;
using NAudio.Wave;

// TODO: experiment with changing system.speech to microsoft.speech.
//  If it works, go this route to avoid memory leak some report

namespace VIOSDotNetClient
{
    public partial class frmMainForm : Form
    {
        private string[] recognizers = { "MicrosoftSpeechRecognizer" };
        private string[] synthesizers = { "MicrosoftSpeechSynthesizer" };
        private string[] players = { "NAudioSoundPlayer" };
        private string[] recorders = { "NAudioSoundRecorder" };

        // These interfaces will be used to manage the selected associated modules
        // The four components represented here provide the VIOS front-end functionality
        private VIOSSpeechRecognizer speechRecognizer = null;
        private VIOSSpeechSynthesizer speechSynthesizer = null;
        private VIOSSoundPlayer soundPlayer = null;
        private VIOSSoundRecorder soundRecorder = null;

        // Names of pair of one-way pipes providing communication with VIOS python-based back-end
        private string pipeFromName = "NPFromGE";
        private string pipeToName = "NPToGE";

        // Named pipe objects
        NamedPipeServerStream pipeFromGameEngine = null;
        NamedPipeServerStream pipeToGameEngine = null;

        // Used for receiving/sending
        BinaryReader br = null;
        BinaryWriter bw = null;

        // Thread to listen for connection
        private Task listenTask = null;

        // Thread to asynchronously read from pipe and queue incoming messages
        private Task pipeReadTask = null;

        // Exclusive thread to manage playing an audio file. Parallel sounds can be played with async call
        private Task audioTask = null;

        private TimeSpan audioCurrentTime = new TimeSpan(0);
        private TimeSpan audioTotalTime = new TimeSpan(0);

        // Thread to manage recording to an audio file
        private Task recordTask = null;

        // This queue allows communication with the audio player thread
        private enum PlayerCmd { None, Stop, Pause, Unpause, Back, Skip, Seek, VolumeSet };        
        private List<KeyValuePair<PlayerCmd, string>> playerCmdQueue = new List<KeyValuePair<PlayerCmd, string>>();

        // Protects audio player command interface
        private Object audioLock = new Object();

        // Protected by playerLock
        private bool audioInProgress = false;

        // Used to protect multi-threaded grammar loading/unloading
        private Object grammarLock = new Object();

        // Used to protect serial pipe transmissions
        private Object writeMsgLock = new Object();

        // Regular grammar
        private Grammar instanceGrammar;

        // A basic "dictation"-mode grammar
        private bool dictationGrammarLoaded = false;
        private DictationGrammar defaultDictationGrammar = new DictationGrammar();

        // Used to indicate when word-stream dictation mode is enabled instead of single-word capture
        private bool dictationMode = false;

        // Variable token instructing recognizer to end dictation when received
        private string endDictationToken = "end dictation";

        // Used to store dictation as it is being assembled
        private string dictationResult = "";

        // Used to signal back to "grammar set" thread that grammar load is completed
        private bool grammarFinishedLoading = true;

        //  Used to create an expanded, false grammar
        //   that can be used to help filter false grammar matches
        private string[] false_choices = { "apple", "bear", "cat", "dog", "elephant",
                                           "funny", "garden", "handy", "island",
                                           "jam", "kelp", "lemon", "melon", "note",
                                           "original", "pear", "queen", "raisin",
                                           "salad", "telephone", "umbrella", "victory",
                                           "weather", "xylophone", "yellow", "zebra" };

        // Microsoft's speech recognition
        private SpeechRecognitionEngine sre = null;

        // Used for recording audio
        private WaveInEvent waveSource = null;
        private WaveFileWriter waveFile = null;

        // Protects audio recording shared data
        private Object recordLock = new Object();

        // Protected by recordLock
        private bool recordInProgress = false;

        // Initialize random number generator poorly
        private Random random = new Random(DateTime.Now.Millisecond);

        private bool interrupted = false;
        private bool soundInterrupted = false;
        private bool recordInterrupted = false;

        public frmMainForm()
        {
            InitializeComponent();

            // Populate module selection comboBoxes

            foreach (string item in recognizers)
            {
                cboSpeechRecognizer.Items.Add(item);
            }

            foreach (string item in synthesizers)
            {
                cboSpeechSynthesizer.Items.Add(item);
            }

            foreach (string item in players)
            {
                cboSoundPlayer.Items.Add(item);
            }

            foreach (string item in recorders)
            {
                cboSoundRecorder.Items.Add(item);
            }

            // Select the first item in each list
            cboSpeechRecognizer.SelectedIndex = 0;
            cboSpeechSynthesizer.SelectedIndex = 0;
            cboSoundPlayer.SelectedIndex = 0;
            cboSoundRecorder.SelectedIndex = 0;

            defaultDictationGrammar.Name = "default dictation";
            defaultDictationGrammar.Enabled = true;

            // Create a new SpeechRecognitionEngine instance.
            sre = new SpeechRecognitionEngine();

            // Set the input of the speech recognizer to the default audio device
            sre.SetInputToDefaultAudioDevice();

            // Register for recognition events
            sre.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(sr_SpeechRecognized);
            sre.RecognizerUpdateReached += new EventHandler<RecognizerUpdateReachedEventArgs>(sr_RecognizerUpdateReached);
        }

        private void LogMsg(string msg)
        {
            txtLog.BeginInvoke((Action)delegate
            {
                txtLog.Text = DateTime.Now.ToFileTimeUtc().ToString() + ": " + msg + System.Environment.NewLine + txtLog.Text;
            });
        }

        private void CreateNode(string path)
        {
            Directory.CreateDirectory(path);
        }

        private void DeleteNode(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path);
            else if (File.Exists(path) && path.EndsWith(".wav"))
                File.Delete(path);
        }

        private void UnlockAudio()
        {
            audioInProgress = false;
        }

        private void UnlockRecord()
        {
            recordInProgress = false;
        }

        private void AudioThreadAsync(string filepath, float volume)
        {
            try
            {
                LogMsg("Starting async audio file ('" + filepath + "') ...");

                IWavePlayer waveOutDevice = new DirectSoundOut();
                AudioFileReader audioFileReader = new AudioFileReader(filepath);

                audioFileReader.Volume = volume;

                try
                {
                    waveOutDevice.Init(audioFileReader);
                    waveOutDevice.Play();
                }
                catch
                {
                    LogMsg("Error starting async audio file ('" + filepath + "').");
                    return;
                }

                while (waveOutDevice.PlaybackState != PlaybackState.Stopped && !interrupted && !soundInterrupted)
                {
                    if (soundInterrupted == false && interrupted == false)
                        Thread.Sleep(500);
                }

                waveOutDevice.Stop();

                audioFileReader.Dispose();
                audioFileReader = null;

                waveOutDevice.Dispose();
                waveOutDevice = null;
            }
            catch (Exception ex)
            {
                LogMsg("Caught exception in AudioThreadAsync. Message: " + ex.Message);
                LogMsg("audio file name = '" + filepath + "'");
                interrupted = true;
            }
        }

        private void AudioThread(string filepath, float volume)
        {
            try
            {
                LogMsg("Starting audio file ('" + filepath + "') ...");

                IWavePlayer waveOutDevice = new DirectSoundOut();
                AudioFileReader audioFileReader = new AudioFileReader(filepath);

                audioFileReader.Volume = volume;

                try
                {
                    waveOutDevice.Init(audioFileReader);
                    waveOutDevice.Play();
                }
                catch
                {
                    LogMsg("Error starting audio file ('" + filepath + "').");
                    UnlockAudio();
                    return;
                }

                audioTotalTime = audioFileReader.TotalTime;

                while (waveOutDevice.PlaybackState != PlaybackState.Stopped && !interrupted && !soundInterrupted)
                {
                    audioCurrentTime = audioFileReader.CurrentTime;

                    KeyValuePair<PlayerCmd, string> cmd = new KeyValuePair<PlayerCmd, string>(PlayerCmd.None, "");
                    lock (audioLock)
                    {
                        if (playerCmdQueue.Count > 0)
                        {
                            cmd = playerCmdQueue[0];
                            playerCmdQueue.RemoveAt(0);
                        }
                    }

                    switch (cmd.Key)
                    {
                        case PlayerCmd.Stop:
                            LogMsg("Player received Stop command.");
                            soundInterrupted = true;
                            break;
                        case PlayerCmd.Pause:
                            LogMsg("Player received Pause command.");
                            waveOutDevice.Pause();
                            break;
                        case PlayerCmd.Unpause:
                            LogMsg("Player received Unpause command.");
                            waveOutDevice.Play();
                            break;
                        case PlayerCmd.Back:
                            LogMsg("Player received Back command.");

                            int numberOfSeconds;
                            try
                            {
                                numberOfSeconds = Int32.Parse(cmd.Value);
                            }
                            catch
                            {
                                LogMsg("AudioThread(): could not parse Back. Value='" + cmd.Value + "'");
                                continue;
                            }

                            audioFileReader.Position -= numberOfSeconds * audioFileReader.WaveFormat.AverageBytesPerSecond;

                            break;
                        case PlayerCmd.Skip:
                            LogMsg("Player received Skip command.");

                            try
                            {
                                numberOfSeconds = Int32.Parse(cmd.Value);
                            }
                            catch
                            {
                                LogMsg("AudioThread(): could not parse Skip. Value='" + cmd.Value + "'");
                                continue;
                            }

                            audioFileReader.Position += numberOfSeconds * audioFileReader.WaveFormat.AverageBytesPerSecond;

                            break;
                        case PlayerCmd.Seek:
                            LogMsg("Player received Seek command.");

                            int percentage;
                            try
                            {
                                percentage = Int32.Parse(cmd.Value);

                                if (percentage < 0 || percentage > 99)
                                    throw new Exception();
                            }
                            catch
                            {
                                LogMsg("AudioThread(): could not parse Seek. Value='" + cmd.Value + "'");
                                continue;
                            }

                            numberOfSeconds = percentage * Convert.ToInt32(audioTotalTime.TotalSeconds / 100);

                            audioFileReader.Position += numberOfSeconds * audioFileReader.WaveFormat.AverageBytesPerSecond;

                            break;
                        case PlayerCmd.VolumeSet:
                            LogMsg("Player received VolumeSet command.");

                            try
                            {
                                volume = float.Parse(cmd.Value);
                            }
                            catch
                            {
                                LogMsg("AudioThread(): could not parse VolumeSet. Value='" + cmd.Value + "'");
                            }

                            audioFileReader.Volume = volume;

                            break;
                    }

                    if (soundInterrupted == false && interrupted == false)
                        Thread.Sleep(500);
                }

                waveOutDevice.Stop();

                audioFileReader.Dispose();
                audioFileReader = null;

                waveOutDevice.Dispose();
                waveOutDevice = null;
            }
            catch (Exception ex)
            {
                LogMsg("Caught exception in AudioThread. Message: " + ex.Message);
                LogMsg("audio file name = '" + filepath + "'");
                interrupted = true;
            }

            UnlockAudio();
        }

        private void SoundPlay(string filepath, float volume)
        {
            if (!File.Exists(filepath))
            {
                LogMsg("SoundPlay(): file (" + filepath + ") does not exist.");
                return;
            }

            if (audioTask != null)
            {
                LogMsg("SoundPlay(): audio thread not yet stopped. Attempting to stop ...");
                SoundStop();
            }

            soundInterrupted = false;
            audioInProgress = true;

            // Clear command queue in order to start fresh
            LogMsg("Clearing audio player command queue ...");
            playerCmdQueue.Clear();

            // Start up audio player
            LogMsg("Starting audio thread ...");
            audioTask = (Task.Factory.StartNew(() => AudioThread(filepath, volume)));

            LogMsg("Audio thread started.");
        }

        private void SoundPlayAsync(string filepath, float volume)
        {
            if (!File.Exists(filepath))
            {
                LogMsg("SoundPlayAsync(): file (" + filepath + ") does not exist.");
                return;
            }

            // Start up audio player
            LogMsg("Starting async audio thread (" + filepath + ") ...");
            Task audioTask = (Task.Factory.StartNew(() => AudioThread(filepath, volume)));

            LogMsg("Async audio thread started.");
        }

        private void SoundStop()
        {
            if (audioTask != null)
            {
                soundInterrupted = true;

                playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Stop, ""));

                audioTask.Wait();
                audioTask = null;

                UnlockAudio();
            }
        }

        void waveSource_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (waveFile != null)
            {
                waveFile.Write(e.Buffer, 0, e.BytesRecorded);
                waveFile.Flush();
            }
        }

        void waveSource_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (waveSource != null)
            {
                waveSource.Dispose();
                waveSource = null;
            }

            if (waveFile != null)
            {
                waveFile.Dispose();
                waveFile = null;
            }
        }

        // This attribute is to allow an AccessViolation generated by NAudio's StopRecording() to be caught
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        private void RecordThread(string filename)
        {
            try
            {
                waveSource = new WaveInEvent();
                waveSource.WaveFormat = new WaveFormat(44100, 1);

                waveSource.DataAvailable += new EventHandler<WaveInEventArgs>(waveSource_DataAvailable);
                waveSource.RecordingStopped += new EventHandler<StoppedEventArgs>(waveSource_RecordingStopped);

                waveFile = new WaveFileWriter(filename, waveSource.WaveFormat);

                LogMsg("Beginning recording ...");

                waveSource.StartRecording();

                while (!recordInterrupted && !interrupted)
                {
                    Thread.Sleep(500);
                }

                LogMsg("Finishing recording ...");

                try
                {
                    waveSource.StopRecording();
                }
                catch
                {
                    LogMsg("Currently catching exception during StopRecording() because of unknown bug, possibly in NAudio.");
                }

                LogMsg("Finished recording.");
            }
            catch (Exception ex)
            {
                LogMsg("Caught exception in RecordThread. Message: " + ex.Message);
                LogMsg("record file name = '" + filename + "'");
                interrupted = true;
            }

            UnlockRecord();

            // Suspend voice recognition updating grammar. Update event will resume voice recognition
            sre.RecognizeAsyncCancel();

            // Load "stop recording" grammar
            LoadInstanceGrammar(instanceGrammar);

            LogMsg("Restored grammar.");
        }

        private void SoundRecord(string filename, string stopArg)
        {
            if (File.Exists(filename))
            {
                LogMsg("Recording (" + filename + ") already exists.");
                return;
            }

            if (recordTask != null)
            {
                LogMsg("Already recording.");
                return;
            }

            recordInterrupted = false;
            recordInProgress = true;

            Dictionary<string, string> dictChoices = new Dictionary<string, string>();
            dictChoices.Add(stopArg, stopArg);

            // Suspend voice recognition updating grammar. Update event will resume voice recognition
            sre.RecognizeAsyncCancel();

            // Load "stop recording" grammar
            LoadInstanceGrammar(BuildGrammar(dictChoices));
 
            // This recording task will restore the previous grammar once it receives the "stop recording" token
            recordTask = (Task.Factory.StartNew(() => RecordThread(filename)));
        }

        private Grammar BuildGrammar(string[] stringChoices)
        {
            Choices choices = new Choices();

            choices.Add(stringChoices);

            GrammarBuilder gb = new GrammarBuilder();
            gb.Append(choices);

            // Return a new Grammar instance.
            return new Grammar(gb);
        }

        private Grammar BuildGrammar(Dictionary<string, string> dictChoices)
        {
            string[] choice_list = dictChoices.Keys.ToArray();

            return BuildGrammar(choice_list);
        }

        private void UnloadAllGrammars()
        {
            // Simply locking here to make sure the SpeechRecognized handler function does not
            //  wake up between sending a message and unloading itself and create a concurrency
            //  problem with the unload
            lock (grammarLock)
            {
                try
                {
                    sre.UnloadAllGrammars();
                }
                catch
                {
                    // Ignore failure to unload (maybe no grammars were loaded?)
                    LogMsg("UnloadGrammar(): ERROR: could not unload (already unloaded?)");
                }

                // Reset all state to indicate no grammars being loaded
                dictationGrammarLoaded = false;
            }
        }

        private void UnloadGrammar()
        {
            lock (grammarLock)
            {
                // Performing this cancel before doing the unload seems
                //  to be necessary to avoid an occasional hangup where
                //  the unload will take a long time to complete
                sre.RecognizeAsyncCancel();

                try
                {
                    sre.UnloadAllGrammars();
                }
                catch
                {
                    // Ignore failure to unload (maybe no grammars were loaded?)
                    LogMsg("UnloadGrammar(): ERROR: could not unload (already unloaded?)");
                }

                dictationGrammarLoaded = false;
            }
        }

        private void LoadInstanceGrammar(Grammar grammar)
        {
            UnloadGrammar();

            grammarFinishedLoading = false;

            lock (grammarLock)
            {
                // Load grammar once recognizer state is paused
                sre.RequestRecognizerUpdate(grammar);

                // Block for grammar load completion
                while (!grammarFinishedLoading)
                {
                    Thread.Sleep(250);
                }
            }
        }

        private void HandleGrammarSet(Message message)
        {
            lock (grammarLock)
            {
                // An empty args string indicates use of the dictation dictionary
                if (message.Args == "")
                {
                    dictationGrammarLoaded = true;

                    // Suspend voice recognition updating grammar. Update event will resume voice recognition
                    sre.RecognizeAsyncCancel();

                    LoadInstanceGrammar(defaultDictationGrammar);
                }
                else
                {
                    // Build a dictionary from the args string
                    string[] choiceElems = message.Args.Split(',');
                    Dictionary<string, string> dictChoices = new Dictionary<string, string>();
                    for (int i = 0; i < choiceElems.Length; ++i)
                    {
                        // Always convert to lower-case
                        string choiceElem = choiceElems[i].ToLower();

                        if (dictChoices.ContainsKey(choiceElem) == false)
                            dictChoices.Add(choiceElem, choiceElem);
                    }

                    // Suspend voice recognition updating grammar. Update event will resume voice recognition
                    sre.RecognizeAsyncCancel();

                    // Build grammar, remember it, then load it
                    instanceGrammar = BuildGrammar(dictChoices);

                    // Only actually load instance grammar, if recording not in progress
                    if (!recordInProgress)
                        LoadInstanceGrammar(instanceGrammar);
                }
            }
        }

        private void StartDictation(string endToken)
        {
            lock (grammarLock)
            {
                endDictationToken = endToken;

                dictationGrammarLoaded = true;

                // Used to indicate word-stream dictation mode rather than single-word capture
                dictationMode = true;

                // Suspend voice recognition updating grammar. Update event will resume voice recognition
                sre.RecognizeAsyncCancel();

                LoadInstanceGrammar(defaultDictationGrammar);
            }
        }

        private void WriteMessageWhenPlayerDone(Message message)
        {
            while (audioInProgress)
            {
                Thread.Sleep(250);
            }

            message.Args = "player done";
            WriteMessage(message);
        }

        private void WriteMessageWhenSynthesisDone(Message message)
        {
            speechSynthesizer.WaitUntilDone();

            message.Args = "synthesis done";
            WriteMessage(message);
        }

        private void WriteMessageWhenRecordDone(Message message)
        {
            while (recordInProgress)
            {
                Thread.Sleep(250);
            }

            message.Args = "record done";
            WriteMessage(message);
        }

        private void HandleCommand(Message message)
        {
            string cmdStr = message.Args;
            string[] cmdElems = cmdStr.Split(',');

            switch (message.Type)
            {
                case "break":
                    speechSynthesizer.Stop();

                    break;
                case "synthesisPause":
                    speechSynthesizer.Pause();

                    break;
                case "synthesisResume":
                    speechSynthesizer.Resume();

                    break;
                case "play":
                    string filepath = cmdElems[0];
                    float volume;
                    try
                    {
                        volume = float.Parse(cmdElems[1]);
                    }
                    catch
                    {
                        LogMsg("Could not parse volume field ('" + cmdElems[1] + "').");
                        return;
                    }

                    if (audioInProgress)
                    {
                        LogMsg("Can't start audio because audio player is currently in use.");
                        return;
                    }
                    
                    SoundPlay(filepath, volume);

                    break;
                case "playAsync":
                    filepath = cmdElems[0];
                    volume = 1.0f;
                    try
                    {
                        volume = float.Parse(cmdElems[1]);
                    }
                    catch
                    {
                        LogMsg("Could not parse volume field ('" + cmdElems[1] + "').");
                        return;
                    }

                    SoundPlayAsync(filepath, volume);

                    break;
                case "playerDone":
                    if (!audioInProgress)
                    {
                        message.Args = "player done";
                        WriteMessage(message);
                    }
                    else
                        Task.Factory.StartNew(() => WriteMessageWhenPlayerDone(message));

                    break;
                case "synthesisDone":
                    Task.Factory.StartNew(() => WriteMessageWhenSynthesisDone(message));

                    break;
                case "recordDone":
                    if (!recordInProgress)
                    {
                        message.Args = "record done";
                        WriteMessage(message);
                    }
                    else
                        Task.Factory.StartNew(() => WriteMessageWhenRecordDone(message));

                    break;
                case "pause":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Pause, ""));
                    }

                    break;
                case "unpause":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Unpause, ""));
                    }

                    break;
                case "stop":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                        {
                            // Make sure audio thread joins
                            SoundStop();
                        }
                    }

                    break;
                case "back":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Back, cmdStr.Split(',')[0]));
                    }

                    break;
                case "skip":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Skip, cmdStr.Split(',')[0]));
                    }

                    break;
                case "seek":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.Seek, cmdStr.Split(',')[0]));
                    }

                    break;
                case "volume":
                    lock (audioLock)
                    {
                        if (audioTask != null)
                            playerCmdQueue.Add(new KeyValuePair<PlayerCmd, string>(PlayerCmd.VolumeSet, cmdStr.Split(',')[0]));
                    }

                    break;
                case "create":
                    CreateNode(cmdStr.Split(',')[0]);

                    break;
                case "delete":
                    DeleteNode(cmdStr.Split(',')[0]);

                    break;
                case "record":
                    string[] cmdStrElems = cmdStr.Split(',');
                    SoundRecord(cmdStrElems[0], cmdStrElems[1]);

                    break;
                case "startDictation":
                    StartDictation(cmdStr.Split(',')[0]);

                    break;
                case "speechSynth":
                    if (speechSynthesizer.InProgress())
                    {
                        LogMsg("HandleCommand(): ERROR: Can't synthesize speech because synthesization is currently in progress.");
                        break;
                    }
                            
                    speechSynthesizer.Synthesize(message.Args, true);

                    break;
                case "grammarSet":
                    HandleGrammarSet(message);

                    break;
            }
        }

        private void ReadMessages(BinaryReader br)
        {
            while (!interrupted)
            {
                try
                {
                    LogMsg("ReadMessages(): waiting for message ..." + System.Environment.NewLine);

                    // Read string length
                    int len = (int)br.ReadUInt32();

                    // Read string
                    string str = new string(br.ReadChars(len));

                    // Shouldn't receive empty messages, but skip if one is received
                    if (str == "")
                    {
                        LogMsg("ReadMessages(): ERROR: empty message. Continuing.");
                        continue;
                    }

                    // Trim off standard message header
                    if (str.StartsWith(">>") == false)
                    {
                        LogMsg("ReadMessages(): ERROR: invalid message header. Continuing.");
                        continue;
                    }
                    str = str.Substring(2);

                    // Trim off standard message footer
                    if (str.EndsWith("<<") == false)
                    {
                        LogMsg("ReadMessages(): ERROR: invalid message footer. Continuing.");
                        continue;
                    }
                    str = str.Substring(0, str.Length - 2);

                    // Parse message and load it into encapsulation class
                    string[] msgElems = str.Split('|');
                    if (msgElems.Length != 4)
                    {
                        LogMsg("ReadMessages(): ERROR: incorrect number of message fields. Continuing.");
                        continue;
                    }
                    Message message = new Message(msgElems[0], msgElems[1], msgElems[2], msgElems[3]);

                    LogMsg("Received message:" + System.Environment.NewLine +
                           "    InstanceId = " + message.InstanceId + System.Environment.NewLine +
                           "    Type = " + message.Type + System.Environment.NewLine +
                           "    MessageId = " + message.MessageId + System.Environment.NewLine +
                           "    Args = " + message.Args + System.Environment.NewLine);

                    // Handle message appropriately depending on type
                    HandleCommand(message);
                }
                catch (Exception ex)
                {
                    LogMsg("ReadMessages(): ERROR: exception '" + ex.Message + "'. Disconnecting ...");

                    LogMsg("Stack trace: " + ex.StackTrace);

                    if (audioTask != null)
                        SoundStop();

                    break;
                }
            }
        }

        // Implementation of a simple serial protocol.
        //  First send 4-byte message length, then send byte stream message
        private void WriteMessage(Message message)
        {
            try
            {
                string serializedMessage = ">>" + message.InstanceId + "|" +
                                                  message.Type + "|" +
                                                  message.MessageId + "|" +
                                                  message.Args + "<<";

                lock (writeMsgLock)
                {
                    byte[] buf = Encoding.ASCII.GetBytes(serializedMessage);

                    // Write buffer length
                    bw.Write((uint)buf.Length);

                    // Write buffer
                    bw.Write(buf);
                }
            }
            catch (Exception ex)
            {
                LogMsg("WriteMessage(): ERROR: message: " + ex.Message);
                return;
            }

            LogMsg("Sent message:" + System.Environment.NewLine +
                   "    InstanceId = " + message.InstanceId + System.Environment.NewLine +
                   "    Type = " + message.Type + System.Environment.NewLine +
                   "    MessageId = " + message.MessageId + System.Environment.NewLine +
                   "    Args = " + message.Args + System.Environment.NewLine);
        }

        private void WaitForConnect()
        {
            while (!interrupted)
            {
                // Create both named pipes so they are available to the back-end
                pipeFromGameEngine = new NamedPipeServerStream(pipeFromName);
                pipeToGameEngine = new NamedPipeServerStream(pipeToName);

                // Open named pipe for receiving
                LogMsg("Waiting for connection (" + pipeFromName + ") ...");
                pipeFromGameEngine.WaitForConnection();
                if (interrupted)
                    continue;
                LogMsg("Connected.");

                // Open named pipe for sending
                LogMsg("Waiting for connection (" + pipeToName + ") ...");
                pipeToGameEngine.WaitForConnection();
                if (interrupted)
                    continue;
                LogMsg("Connected.");

                br = new BinaryReader(pipeFromGameEngine);
                bw = new BinaryWriter(pipeToGameEngine);

                // Initialize speech recognition with filter set of "false" choices
                sre.LoadGrammar(BuildGrammar(false_choices));

                // Start thread which will pull messages from the incoming pipe and add them to the message queue
                LogMsg("Starting reader thread ...");
                pipeReadTask = (Task.Factory.StartNew(() => ReadMessages(br)));
                LogMsg("Finished starting thread.");

                pipeReadTask.Wait();

                br.Close();
                br.Dispose();
                br = null;

                bw.Close();
                bw.Dispose();
                bw = null;

                // Wait for any in-progress speech synthesis to complete before continuing
                speechSynthesizer.WaitUntilDone();

                // Reset all speech recognition state
                sre.RecognizeAsyncCancel();
                sre.UnloadAllGrammars();
                dictationGrammarLoaded = false;
            }

            LogMsg("Stopped.");
        }

        private void Stop()
        {
            SoundStop();

            interrupted = true;

            if (pipeFromGameEngine != null)
            {
                pipeFromGameEngine.Dispose();
                pipeFromGameEngine = null;

                using (NamedPipeClientStream npcs = new NamedPipeClientStream(pipeFromName))
                {
                    try
                    {
                        npcs.Connect(100);
                    }
                    catch
                    {
                    }
                }
            }

            if (pipeToGameEngine != null)
            {
                pipeToGameEngine.Dispose();
                pipeToGameEngine = null;

                using (NamedPipeClientStream npcs = new NamedPipeClientStream(pipeToName))
                {
                    try
                    {
                        npcs.Connect(100);
                    }
                    catch
                    {
                    }
                }
            }

            listenTask.Wait();

            btnStart.Enabled = true;
            btnStop.Enabled = false;
        }

        private void sr_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            lock (grammarLock)
            {
                string text = e.Result.Text.ToLower().Trim();

                // Ignore matches against the filter "false" choices
                if (false_choices.Contains(text))
                {
                    LogMsg("False match: " + text);
                    return;
                }

                // Simple mechanism for interrupting recording once "stop recording" grammar token is received
                if (recordInProgress)
                {
                    recordInterrupted = true;
                    return;
                }

                string responseType = "grammarMatch";

                if (dictationMode)
                {
                    dictationResult += " " + text;

                    // Just return if dictation not yet complete (no end token received yet)
                    if (dictationResult.Contains(endDictationToken.ToLower()) == false)
                        return;

                    // Get ready to return entire dictation result
                    text = dictationResult.TrimStart();

                    // Unload dictation grammar
                    // TODO: may throw exception doing unload from within this event. If so, will have to change, maybe keep dictationMode on,
                    //  but set endToken to "" so that can be tested for and the function just returned from if so
                    UnloadGrammar();

                    dictationMode = false;

                    responseType = "dictationResult";
                }

                // .Net-code no longer needs to worry about InstanceId or MessageId for grammarMatches or dictationMatches
                // This association is now handled strictly on the python-side for grammarMatches or dictationMatches

                Message message;
                if (dictationGrammarLoaded)
                {
                    // Send the first valid word detected to satisfy the single-word dictation-grammar mode (not dictation mode)
                    message = new Message("1", responseType, "1", text.Split()[0]);
                }
                else
                    message = new Message("1", responseType, "1", text);

                try
                {
                    WriteMessage(message);
                }
                catch
                {
                    LogMsg("Caught exception during sr_SpeechRecognized event.");
                }
            }
        }

        private void sr_RecognizerUpdateReached(object sender, RecognizerUpdateReachedEventArgs e)
        {
            sre.LoadGrammar((Grammar)e.UserToken);

            sre.RecognizeAsync(RecognizeMode.Multiple);

            grammarFinishedLoading = true;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            interrupted = false;

            if (cboSpeechSynthesizer.Text == "MicrosoftSpeechSynthesizer")
                speechSynthesizer = new MicrosoftSpeechSynthesizer();

            listenTask = (Task.Factory.StartNew(() => WaitForConnect()));

            btnStart.Enabled = false;
            btnStop.Enabled = true;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            Stop();
        }
    }
}
