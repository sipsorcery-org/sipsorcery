//-----------------------------------------------------------------------------
// Filename: FormAudioScopeDesigner.cs
//
// Description: Initialise the Winfows Form portion of the Audio Scope demo.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace AudioScope
{
    partial class FormAudioScope
    {
        private System.ComponentModel.IContainer components = null;

        private SharpGL.OpenGLControl openGLControl1;
        private SharpGL.OpenGLControl openGLControl2;

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

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormAudioScope));
            this.openGLControl1 = new SharpGL.OpenGLControl();
            this.openGLControl2 = new SharpGL.OpenGLControl();

            ((System.ComponentModel.ISupportInitialize)(this.openGLControl1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.openGLControl2)).BeginInit();

            this.SuspendLayout();

            this.openGLControl1.DrawFPS = false;
            this.openGLControl1.Name = "openGLControl1";
            this.openGLControl1.OpenGLVersion = SharpGL.Version.OpenGLVersion.OpenGL2_1;
            this.openGLControl1.RenderContextType = SharpGL.RenderContextType.FBO;
            this.openGLControl1.RenderTrigger = SharpGL.RenderTrigger.Manual;
            this.openGLControl1.Size = new System.Drawing.Size(AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);
            this.openGLControl1.TabIndex = 0;
            this.openGLControl1.OpenGLInitialized += new System.EventHandler(this.OpenGLControl1_OpenGLInitialized);
            this.openGLControl1.OpenGLDraw += new SharpGL.RenderEventHandler(this.openGLControl1_OpenGLDraw);
            this.openGLControl1.Dock = System.Windows.Forms.DockStyle.Left;

            this.openGLControl2.DrawFPS = false;
            this.openGLControl2.Name = "openGLControl2";
            this.openGLControl2.OpenGLVersion = SharpGL.Version.OpenGLVersion.OpenGL2_1;
            this.openGLControl2.RenderContextType = SharpGL.RenderContextType.FBO;
            this.openGLControl2.RenderTrigger = SharpGL.RenderTrigger.Manual;
            this.openGLControl2.Size = new System.Drawing.Size(AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);
            this.openGLControl2.TabIndex = 1;
            this.openGLControl2.OpenGLInitialized += new System.EventHandler(this.OpenGLControl2_OpenGLInitialized);
            this.openGLControl2.OpenGLDraw += new SharpGL.RenderEventHandler(this.openGLControl2_OpenGLDraw);
            this.openGLControl2.Dock = System.Windows.Forms.DockStyle.Fill;

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(AUDIO_SCOPE_WIDTH * 2, AUDIO_SCOPE_HEIGHT);
            this.Controls.Add(this.openGLControl2);
            this.Controls.Add(this.openGLControl1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FormAudioScopeSample";
            this.Text = "Audio Scope Sample";

            ((System.ComponentModel.ISupportInitialize)(this.openGLControl1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.openGLControl2)).EndInit();
            this.ResumeLayout(false);
        }
    }
}

