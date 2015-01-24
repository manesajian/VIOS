using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VIOSDotNetClient
{
    interface VIOSSoundRecorder
    {
        void Start();

        void Stop();

        void WaitUntilDone();
    }

    interface VIOSSoundPlayer
    {
        void Volume(float volume);

        void Play(string filepath, float volume);

        void PlayAsync(string filepath, float volume);

        void Pause();

        void Resume();

        void Back(int seconds);

        void Skip(int seconds);

        void Seek(int percentage);

        void Stop();

        void WaitUntilDone();
    }

    interface VIOSSpeechRecognizer
    {
        void GrammarSet(List<string> choices);

        void StartDictation();
    }

    interface VIOSSpeechSynthesizer
    {
        void Synthesize(string text, bool async);

        void Pause();

        void Resume();

        void Stop();

        bool InProgress();

        void WaitUntilDone();
    }
}