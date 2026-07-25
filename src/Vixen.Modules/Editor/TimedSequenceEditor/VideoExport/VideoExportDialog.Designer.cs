namespace VixenModules.Editor.TimedSequenceEditor.VideoExport
{
	partial class VideoExportDialog
	{
		private System.ComponentModel.IContainer components = null;

		private System.Windows.Forms.TextBox txtOutputPath;
		private System.Windows.Forms.Button btnBrowse;
		private System.Windows.Forms.Label lblOutputPath;
		private System.Windows.Forms.Label lblFrameRate;
		private System.Windows.Forms.ComboBox cbFrameRate;
		private System.Windows.Forms.CheckBox chkIncludeAudio;
		private System.Windows.Forms.Label lblEncoder;
		private System.Windows.Forms.ComboBox cbEncoder;
		private System.Windows.Forms.ProgressBar progressBar;
		private System.Windows.Forms.Label lblStatus;
		private System.Windows.Forms.Button btnStart;
		private System.Windows.Forms.Button btnCancel;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.lblOutputPath = new System.Windows.Forms.Label();
			this.txtOutputPath = new System.Windows.Forms.TextBox();
			this.btnBrowse = new System.Windows.Forms.Button();
			this.lblFrameRate = new System.Windows.Forms.Label();
			this.cbFrameRate = new System.Windows.Forms.ComboBox();
			this.chkIncludeAudio = new System.Windows.Forms.CheckBox();
			this.lblEncoder = new System.Windows.Forms.Label();
			this.cbEncoder = new System.Windows.Forms.ComboBox();
			this.progressBar = new System.Windows.Forms.ProgressBar();
			this.lblStatus = new System.Windows.Forms.Label();
			this.btnStart = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this.SuspendLayout();

			// lblOutputPath
			this.lblOutputPath.AutoSize = true;
			this.lblOutputPath.Location = new System.Drawing.Point(12, 15);
			this.lblOutputPath.Name = "lblOutputPath";
			this.lblOutputPath.Size = new System.Drawing.Size(66, 15);
			this.lblOutputPath.Text = "Output File:";

			// txtOutputPath
			this.txtOutputPath.Location = new System.Drawing.Point(12, 33);
			this.txtOutputPath.Name = "txtOutputPath";
			this.txtOutputPath.Size = new System.Drawing.Size(420, 23);

			// btnBrowse
			this.btnBrowse.Location = new System.Drawing.Point(438, 32);
			this.btnBrowse.Name = "btnBrowse";
			this.btnBrowse.Size = new System.Drawing.Size(80, 25);
			this.btnBrowse.Text = "Browse...";
			this.btnBrowse.UseVisualStyleBackColor = true;
			this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);

			// lblFrameRate
			this.lblFrameRate.AutoSize = true;
			this.lblFrameRate.Location = new System.Drawing.Point(12, 75);
			this.lblFrameRate.Name = "lblFrameRate";
			this.lblFrameRate.Text = "Frame Rate:";

			// cbFrameRate
			this.cbFrameRate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbFrameRate.FormattingEnabled = true;
			this.cbFrameRate.Items.AddRange(new object[] { "24", "30", "60" });
			this.cbFrameRate.Location = new System.Drawing.Point(110, 72);
			this.cbFrameRate.Name = "cbFrameRate";
			this.cbFrameRate.Size = new System.Drawing.Size(80, 23);

			// chkIncludeAudio
			this.chkIncludeAudio.AutoSize = true;
			this.chkIncludeAudio.Location = new System.Drawing.Point(220, 74);
			this.chkIncludeAudio.Name = "chkIncludeAudio";
			this.chkIncludeAudio.Size = new System.Drawing.Size(105, 19);
			this.chkIncludeAudio.Text = "Include Audio";
			this.chkIncludeAudio.UseVisualStyleBackColor = true;

			// lblEncoder
			this.lblEncoder.AutoSize = true;
			this.lblEncoder.Location = new System.Drawing.Point(12, 108);
			this.lblEncoder.Name = "lblEncoder";
			this.lblEncoder.Text = "Encoder:";

			// cbEncoder
			this.cbEncoder.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbEncoder.FormattingEnabled = true;
			this.cbEncoder.Location = new System.Drawing.Point(110, 105);
			this.cbEncoder.Name = "cbEncoder";
			this.cbEncoder.Size = new System.Drawing.Size(300, 23);

			// progressBar
			this.progressBar.Location = new System.Drawing.Point(12, 140);
			this.progressBar.Name = "progressBar";
			this.progressBar.Size = new System.Drawing.Size(506, 23);

			// lblStatus
			this.lblStatus.AutoSize = true;
			this.lblStatus.Location = new System.Drawing.Point(12, 170);
			this.lblStatus.Name = "lblStatus";
			this.lblStatus.Size = new System.Drawing.Size(0, 15);

			// btnStart
			this.btnStart.Location = new System.Drawing.Point(334, 200);
			this.btnStart.Name = "btnStart";
			this.btnStart.Size = new System.Drawing.Size(90, 30);
			this.btnStart.Text = "Start Export";
			this.btnStart.UseVisualStyleBackColor = true;
			this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

			// btnCancel
			this.btnCancel.Location = new System.Drawing.Point(430, 200);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(90, 30);
			this.btnCancel.Text = "Close";
			this.btnCancel.UseVisualStyleBackColor = true;
			this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

			// VideoExportDialog
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(534, 245);
			this.Controls.Add(this.lblOutputPath);
			this.Controls.Add(this.txtOutputPath);
			this.Controls.Add(this.btnBrowse);
			this.Controls.Add(this.lblFrameRate);
			this.Controls.Add(this.cbFrameRate);
			this.Controls.Add(this.chkIncludeAudio);
			this.Controls.Add(this.lblEncoder);
			this.Controls.Add(this.cbEncoder);
			this.Controls.Add(this.progressBar);
			this.Controls.Add(this.lblStatus);
			this.Controls.Add(this.btnStart);
			this.Controls.Add(this.btnCancel);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "VideoExportDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Export Preview Video";
			this.ResumeLayout(false);
			this.PerformLayout();
		}
	}
}
