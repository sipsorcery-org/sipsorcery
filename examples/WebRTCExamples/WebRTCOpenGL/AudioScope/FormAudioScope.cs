//-----------------------------------------------------------------------------
// Filename: FormAudioScope.cs
//
// Description: Initialise the Windows Form portion of the Audio Scope demo.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Windows.Forms;
using SharpGL;

namespace AudioScope
{
    public partial class FormAudioScope : Form
    {
        public const int AUDIO_SCOPE_WIDTH = 640;
        public const int AUDIO_SCOPE_HEIGHT = 480;
        public const int BYTES_PER_PIXEL = 3; // RGB = 3 bytes per pixel.

        private bool _showWindow;
        private AudioScope _audioScope;
        private AudioScopeOpenGL _audioScopeGL;
        private byte[] _pixelData;

        /// <summary>
        /// Note: If you want to see the Windows Form with the Audio Scope comment out this whole method.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;

                if (!_showWindow)
                {
                    // Remove WS_EX_APPWINDOW to ensure it won't appear in Alt+Tab
                    cp.ExStyle &= ~0x00040000; // WS_EX_APPWINDOW

                    // Add WS_EX_TOOLWINDOW to hide from Alt+Tab
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW

                    // Ensure the window is layered (required for transparency)
                    cp.ExStyle |= 0x00080000; // WS_EX_LAYERED

                    // Make it transparent to mouse events
                    cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                }

                return cp;
            }
        }

        public FormAudioScope(bool showWindow)
        {
            _showWindow = showWindow;

            _audioScope = new AudioScope();
            _audioScopeGL = new AudioScopeOpenGL(_audioScope);

            _pixelData = new byte[AUDIO_SCOPE_WIDTH * AUDIO_SCOPE_HEIGHT * BYTES_PER_PIXEL];

            InitializeComponent();

            if (!showWindow)
            {
                this.ShowInTaskbar = false;        // remove from the taskbar
                this.FormBorderStyle = FormBorderStyle.None;
                this.Opacity = 0;                 // invisible, but "active" 
                this.StartPosition = FormStartPosition.Manual;
                this.Enabled = false;
            }
        }

        public byte[] ProcessAudioSample(Complex[] samples)
        {
            _audioScope.ProcessSample(samples);

            openGLControl1.DoRender();

            this.openGLControl1.OpenGL.ReadPixels(0, 0, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, _pixelData);

            // The pizel buffer is upside down at this point. Because the audio scope is a circular pattern the effect of the flip is inconsequential
            // so the processing effort is skipped.

            return _pixelData;
        }

        private void OpenGLControl_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Initialise(gl);
        }

        private void openGLControl1_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Draw(gl, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);

            gl.Finish();
        }
    }
}
