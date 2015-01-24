namespace VIOSDotNetClient
{
    partial class frmMainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnStop = new System.Windows.Forms.Button();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.cboSpeechRecognizer = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.cboSpeechSynthesizer = new System.Windows.Forms.ComboBox();
            this.cboSoundPlayer = new System.Windows.Forms.ComboBox();
            this.cboSoundRecorder = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // btnStop
            // 
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(532, 31);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(75, 23);
            this.btnStop.TabIndex = 11;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(4, 60);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(629, 373);
            this.txtLog.TabIndex = 7;
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(532, 4);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(75, 23);
            this.btnStart.TabIndex = 6;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // cboSpeechRecognizer
            // 
            this.cboSpeechRecognizer.FormattingEnabled = true;
            this.cboSpeechRecognizer.Location = new System.Drawing.Point(112, 6);
            this.cboSpeechRecognizer.Name = "cboSpeechRecognizer";
            this.cboSpeechRecognizer.Size = new System.Drawing.Size(138, 21);
            this.cboSpeechRecognizer.TabIndex = 12;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(5, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(101, 13);
            this.label1.TabIndex = 13;
            this.label1.Text = "Speech Recognizer";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(256, 9);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(101, 13);
            this.label2.TabIndex = 14;
            this.label2.Text = "Speech Synthesizer";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(36, 36);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(70, 13);
            this.label3.TabIndex = 15;
            this.label3.Text = "Sound Player";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(272, 36);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(85, 13);
            this.label4.TabIndex = 16;
            this.label4.Text = "Sound Recorder";
            // 
            // cboSpeechSynthesizer
            // 
            this.cboSpeechSynthesizer.FormattingEnabled = true;
            this.cboSpeechSynthesizer.Location = new System.Drawing.Point(363, 6);
            this.cboSpeechSynthesizer.Name = "cboSpeechSynthesizer";
            this.cboSpeechSynthesizer.Size = new System.Drawing.Size(138, 21);
            this.cboSpeechSynthesizer.TabIndex = 17;
            // 
            // cboSoundPlayer
            // 
            this.cboSoundPlayer.FormattingEnabled = true;
            this.cboSoundPlayer.Location = new System.Drawing.Point(112, 33);
            this.cboSoundPlayer.Name = "cboSoundPlayer";
            this.cboSoundPlayer.Size = new System.Drawing.Size(138, 21);
            this.cboSoundPlayer.TabIndex = 18;
            // 
            // cboSoundRecorder
            // 
            this.cboSoundRecorder.FormattingEnabled = true;
            this.cboSoundRecorder.Location = new System.Drawing.Point(363, 33);
            this.cboSoundRecorder.Name = "cboSoundRecorder";
            this.cboSoundRecorder.Size = new System.Drawing.Size(138, 21);
            this.cboSoundRecorder.TabIndex = 19;
            // 
            // frmMainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(637, 437);
            this.Controls.Add(this.cboSoundRecorder);
            this.Controls.Add(this.cboSoundPlayer);
            this.Controls.Add(this.cboSpeechSynthesizer);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.cboSpeechRecognizer);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.btnStart);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Name = "frmMainForm";
            this.Text = "VIOS .Net Client";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.ComboBox cboSpeechRecognizer;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox cboSpeechSynthesizer;
        private System.Windows.Forms.ComboBox cboSoundPlayer;
        private System.Windows.Forms.ComboBox cboSoundRecorder;
    }
}

