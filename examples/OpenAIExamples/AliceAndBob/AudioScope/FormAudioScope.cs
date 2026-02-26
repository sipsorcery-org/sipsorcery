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
        public const int AUDIO_SCOPE_WIDTH = 400;
        public const int AUDIO_SCOPE_HEIGHT = 400;
        public const int BYTES_PER_PIXEL = 3; // RGB = 3 bytes per pixel.

        private bool _showWindow;
        private AudioScope _audioScope1;
        private AudioScopeOpenGL _audioScopeGL1;
        private byte[] _pixelData1;
        private AudioScope _audioScope2;
        private AudioScopeOpenGL _audioScopeGL2;
        private byte[] _pixelData2;

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

            _audioScope1 = new AudioScope();
            _audioScopeGL1 = new AudioScopeOpenGL(_audioScope1);

            _pixelData1 = new byte[AUDIO_SCOPE_WIDTH * AUDIO_SCOPE_HEIGHT * BYTES_PER_PIXEL];

            _audioScope2 = new AudioScope();
            _audioScopeGL2 = new AudioScopeOpenGL(_audioScope2);

            _pixelData2 = new byte[AUDIO_SCOPE_WIDTH * AUDIO_SCOPE_HEIGHT * BYTES_PER_PIXEL];

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

        public byte[] ProcessAudioSample(Complex[] samples, int scopeNumber)
        {
            if (scopeNumber == 1)
            {
                _audioScope1.ProcessSample(samples);

                openGLControl1.DoRender();

                this.openGLControl1.OpenGL.ReadPixels(0, 0, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, _pixelData1);

                // The pizel buffer is upside down at this point. Because the audio scope is a circular pattern the effect of the flip is inconsequential
                // so the processing effort is skipped.

                return _pixelData1;
            }
            else if (scopeNumber == 2)
            {
                _audioScope2.ProcessSample(samples);

                openGLControl2.DoRender();

                this.openGLControl2.OpenGL.ReadPixels(0, 0, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT, OpenGL.GL_RGB, OpenGL.GL_UNSIGNED_BYTE, _pixelData2);

                // The pizel buffer is upside down at this point. Because the audio scope is a circular pattern the effect of the flip is inconsequential
                // so the processing effort is skipped.

                return _pixelData2;
            }
            else
            {
                throw new ApplicationException("Invalid scope number.");
            }
        }

        private void OpenGLControl1_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL1.Initialise(gl);
        }

        private void openGLControl1_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL1.Draw(gl, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);

            gl.Finish();
        }

        private void OpenGLControl2_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl2.OpenGL;
            _audioScopeGL2.Initialise(gl);
        }

        private void openGLControl2_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl2.OpenGL;
            _audioScopeGL2.Draw(gl, AUDIO_SCOPE_WIDTH, AUDIO_SCOPE_HEIGHT);

            gl.Finish();
        }
    }
}
