using System;
using System.Windows.Forms;
using SharpGL;

namespace AudioScope
{
    public partial class FormAudioScope : Form
    {
        private AudioScope _audioScope;
        private AudioScopeOpenGL _audioScopeGL;

        public FormAudioScope()
        {
            _audioScope = new AudioScope();
            _audioScopeGL = new AudioScopeOpenGL(_audioScope);

            InitializeComponent();
        }

        private void FormAudioScope_Load(object sender, EventArgs e)
        {
            //_audioScope.InitAudio(AudioSourceEnum.Simulation);
            _audioScope.InitAudio(AudioSourceEnum.NAudio);
            //_audioScope.InitAudio(AudioSourceEnum.PortAudio);
            _audioScope.Start();
        }

        private void OpenGLControl_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Initialise(gl);
        }

        private void openGLControl1_OpenGLDraw(object sender, RenderEventArgs e)
        {
            OpenGL gl = this.openGLControl1.OpenGL;
            _audioScopeGL.Draw(gl, this.openGLControl1.Width, this.openGLControl1.Height);
        }
    }
}
