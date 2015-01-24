using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Speech.Synthesis;

namespace VIOSDotNetClient
{
    public class MicrosoftSpeechSynthesizer : VIOSSpeechSynthesizer
    {
        private SpeechSynthesizer synthesizer = new SpeechSynthesizer();

        public bool speechInProgress = false;

        public MicrosoftSpeechSynthesizer()
        {
            // Register for speech synthesis completion event
            synthesizer.SpeakCompleted += sr_SpeakCompleted;
        }

        private void sr_SpeakCompleted(object sender, SpeakCompletedEventArgs e)
        {
            speechInProgress = false;
        }

        public void Synthesize(string speech, bool async = false)
        {
            if (speechInProgress)
                throw new Exception("MicrosoftSpeechSynthesizer.Synthesize(): ERROR: synthesis in progress.");
      
            speechInProgress = true;
            
            synthesizer.SpeakAsync(speech);

            if (async == false)
                WaitUntilDone();
        }

        public void Pause()
        {
            if (speechInProgress)
            {
                try
                {
                    synthesizer.Pause();
                }
                catch
                {
                }
            }
        }

        public void Resume()
        {
            if (speechInProgress)
            {
                try
                {
                    synthesizer.Resume();
                }
                catch
                {
                }
            }
        }

        public void Stop()
        {
            if (speechInProgress)
            {
                try
                {
                    if (synthesizer.State == SynthesizerState.Paused)
                        synthesizer.Resume();
                    synthesizer.SpeakAsyncCancelAll();
                }
                catch
                {
                }
            }
        }

        public bool InProgress()
        {
            return speechInProgress;
        }

        public void WaitUntilDone()
        {
            while (speechInProgress)
            {
                Thread.Sleep(500);
            }
        }
    }
}
