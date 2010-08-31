README ------------------------------------------------------------------------
  http://silverlight.net/dlr
  http://codeplex.com/sdlsdk

  The "Silverlight Dynamic Languages SDK" is the integration between Silverlight 
  and Dynamic Languages running on the Dynamic Language Runtime (DLR). The 
  languages included in this package are IronRuby, IronPython, and Managed
  JScript.

  Note: this package is meant to be used to develop applications with 
  Silverlight 2 Beta 2. To install Silverlight, visit the following:

  http://www.microsoft.com/silverlight/resources/install.aspx?v=2.0 

Package -----------------------------------------------------------------------

  /bin: IronPython, IronRuby, and Managed JScript language/library DLLS, as well 
  as Chiron, Silverlight's localhost development utility. See the Chiron section 
  below for Chiron's usage.

  /script: tools for creating and running Silverlight applications

  /License.txt: Microsoft Public License

  /Readme.txt: This file.

  Samples can be found on http://codeplex.com/sdlsdk and
  http://silverlight.net/dlr. Simply unzip package into this directory.

  Source Code can be found on http://codeplex.com/sdlsdk. Simply unzip package
  into this directory.

Getting Started ---------------------------------------------------------------

  ===============================================================================
  1. Create a new Silverlight application
  2. Run an application
  ===============================================================================
  
  1. Create a new Silverlight application

    > script/sl [ruby|python|jscript] <application_name>

    This will create a Silverlight application in the language of your choosing in
    the "application_name" directory where you ran the script.

    This command requires Ruby to be installed.
      Windows:  "One-Click Installer" for Ruby: http://rubyforge.org/frs/?group_id=167
      Mac OS X: Ships with Ruby pre-installed.

  2. Run an application

    > script/server /b

    This will launch Chiron, the Silverlight development utility, as well as open
    your default browser to http://localhost:2060. If you pass the /w
    instead of the /b switch, it will just start the server and not launch your
    browser. See the Chiron section below for more of its usage details.

    Note: Place </path/to>/sdlsdk/script on your PATH to omit the "script/" from
          these commands.
          
    This command requires Mono to be installed on the Mac.
    http://www.go-mono.com/mono-downloads/download.html.
    
Chiron ------------------------------------------------------------------------

  Chiron.exe - Silverlight Development Utility

  Chiron is a command-line utility for creating Silverlight XAP files and
  enabling package-less development of dynamic language applications.

  ==============================================================================
  1. Creating Silverlight XAP files
     a. XAP a C#/VB application
     b. XAP a DLR application
  2. Package-less development of DLR applications
  3. Configuration
  4. Documentation 
  ==============================================================================

  1. Creating Silverlight XAP files

    Chiron can produce Silverlight application packages (XAP) for C#/VB
    applications, as well as any Dynamic Language Runtime (DLR) based language.

    Dynamic language applications require the DLR and language assemblies to be
    inside the package as well. To do this automatically:

    >Chiron.exe /directory:MyApp\app /zipdlr:app.xap

    This will XAP the app directory, generate an AppManifest.xaml if necessary, 
    and insert the DLR assemblies and the language assemblies of the languages 
    you usedin your app.

    Given this simple application:

    MyApp\
    MyApp\index.html
    MyApp\app\
    MyApp\app\app.rb

    Chiron will generate an app.xap (in the MyApp folder) with these contents:

    app.rb
    AppManifest.xaml
    IronRuby.dll
    IronRuby.Libraries.dll
    Microsoft.Scripting.dll
    Microsoft.Scripting.Silverlight.dll

  2. Package-less development of DLR applications

    Generating a XAP file works great for compiled languages since they can do it
    as a post-build step. However, dynamic languages are traditionally not 
    compiled to disk, so Chiron automates the XAP file creation with every 
    web-request.

    So, with our same sample app:

    >cd MyApp
    >Chiron.exe /webserver

    Chiron will launch a localhost-only web-server at http://localhost:2060 with
    the webroot being the current working directory. When it receives a request 
    for "app.xap", it will look for a folder named "app", XAP the contents (same 
    behavior as the /zipdlr flag) in memory, and send it back to the client.

    This allows for the developer to edit a file and simply hit refresh on their
    browser to see the change.

  3. Configuration

    Chiron allows you to configure what languages it knows about and the
    AppManifest.xaml template by modifying Chiron.exe.config.

    New languages can be added in the <languages> section as follows:

    <language extensions="myext"
              assemblies="MyLanguageRuntime.dll"
              languageContext="MyLanguage.Namespace.MyLanguageContextClass" />

    Customizations to the AppManifest template go in the <appManifestTemplate>
    section.

  4. Documentation

    Running Chiron without any command-line arguments will show you the avaliable
    flags Chiron will accept.
    
    >Chiron.exe
    Microsoft(R) Silverlight(TM) Development Utility. Version 1.0.0.0
    Copyright (c) Microsoft Corporation.  All rights reserved.

    Usage: Chiron [<options>]

    General Options:

      /d[irectory]:<path>
        Specifies directory on disk (default: the current directory)

      /x[ap]:<file>
        Specifies XAP file to generate
        Does not start the web server, cannot be combined with /w or /b

      /n[ologo]
        Suppresses display of the logo banner

      /s[ilent]
        Suppresses display of all output

    Dynamic Language Options:

      /z[ipdlr]:<file>
        Like /x, but includes files needed for dyanmic language apps
        Does not start the web server, cannot be combined with /w or /b

      /w[ebserver][:<port number>]
        Launches a development web server that automatically creates
        XAP files for dynamic language applications
        Optionally specifies server port number (default: 2060)

      /b[rowser]
        Launches the default browser and starts the web server
        Implies /w, cannot be combined with /x or /z

      /m[anifest]
        Saves the generated AppManifest.xaml file to disk
        Use /d to set the directory containing the sources
        Can only be combined with /d, /n and /s

History -----------------------------------------------------------------------

  March 7, 2008 - MIX08 release for Silverlight 2; IronRuby and IronPython support
                  for Silverlight 2 Beta 1.
  May 6, 2008   - Adds Managed JScript to the package, as well as the "sl" command
  June 9, 2008  - Release for Silverlight 2 Beta 2. Removes samples and source
                  code from main project to seperate downloads on
                  http://codeplex.com/sdlsdk.
  June 11, 2008 - Now script/sl.bat does not depend on Ruby being installed
