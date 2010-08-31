
#Simple prototype of a dynamic language console and editor
#by Jim Hugunin

#Set to true for larger fonts for demos
demoMode = False

#Selects the initial language
lang = 'Python'
#Can run some tests at the start of the console
autoTest = False

from System import *
import wpf
from wpf import *

import Microsoft
import sys, clr

clr.AddReference('Microsoft.Scripting')

from Microsoft.Scripting.Hosting import *      
from Microsoft.Scripting import SourceLocation, SourceSpan
scriptEnv = ScriptEnvironment.GetEnvironment()
            
languages = ['Python', 'Ruby', 'JScript']

intro = """\
Interactive DLR console - click to select and type code.
See box below for suggestions and to change language.
"""

aboutText = """\
I hope that you'll have fun playing with this toy to \
explore both Silverlight and the dynamic languages it \
supports.  This is still just a toy with a wealth of \
missing features - most \
notably is no support for any control-keys except for \
the magic control-enter to execute each of the buffers. \
Still, I had a great time writing this to explore the \
Silverlight platform and hope that you'll have as much \
fun playing here.

More samples of using dynamic languages from Silverlight \
are here:"""

linkText = r"http://codeplex.com/dynamicsilverlight"


py_pre = """\
import wpf
canvas.Background = wpf.RedBrush
r= wpf.TextBlock()
r.Text="Click Me"
canvas.Children.Add(r)
r.FontSize = 100
wpf.SetPosition(r, 40, 60)
def doit(b, e): print 'Ouch!'
r.MouseLeftButtonDown += doit"""


js_pre = """\
canvas.Background = wpf.RedBrush;
r = new wpf.TextBlock();
r.Text = "Click Me";
canvas.Children.Add(r);
r.FontSize = 100;
wpf.SetPosition(r, 40 ,60);
function doit() { write("Ouch!"); }
r.MouseLeftButtonDown += doit;"""


rb_pre = """\
require 'wpf'
canvas.background = SolidColorBrush.new Colors.red
$r = TextBlock.new
$r.text = 'Click Me'
canvas.children.add $r
$r.font_size = 100
$r.left, $r.top = 40, 60
r.mouse_left_button_down { |s,e| puts 'Ouch!' }"""


xaml_test = """<!--Edit XAML here.  Type Control+Enter to reload after editing-->
<Canvas xmlns="http://schemas.microsoft.com/client/2007"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

<TextBlock x:Name="text1"
           Text="Silverlight Canvas"
           FontSize="24"/>

</Canvas>"""

py_code_test = """\
#Type Control+Enter to execute code
import wpf

canvas.Background = wpf.BlueBrush

r = wpf.TextBlock()
r.Text="Touch Me"
canvas.Children.Add(r)
r.FontSize = 60
wpf.SetPosition(r, 40, 60)

def light(s, e):
  s.Foreground = wpf.SolidColorBrush(wpf.Colors.White)

def dark(s, e):
  s.Foreground = wpf.SolidColorBrush(wpf.Colors.Black)

r.MouseEnter += light
r.MouseLeave += dark"""

js_code_test = """\
m = LoadModule('lib/3dtext')

canvas.Children.Clear()
canvas.Background = wpf.WhiteBrush

xaml = m.xaml

m.Application.Current.LoadComponent(m.xaml, "lib/3dtext.xaml")
xaml.Loaded += m.init

Import("System")

c = new System.Windows.Controls.Canvas
c.Children.Add(xaml)
canvas.Children.Add(c)

"""

rb_code_test = """\
require 'lib/script'
$c = CoolDemo.new.show_clock.set_time_to now
Drag.new($c.clock.canvas).enable
"""


class LanguageEngine:
    def __init__(self, engine, test, code_test, longName, shortName, color):
        self.engine = engine
        self.test = test
        self.code_test = code_test
        self.longName = longName
        self.shortName = shortName
        self.color = color
        
    def MakePrompt(self, style, initial):
        tb = wpf.TextBlock()
        run = style.MakeRun()
        if initial: run.Text = self.shortName + "> "
        else: run.Text = self.shortName + "| "
        run.Foreground = wpf.SolidColorBrush(self.color)
        if tb.Inlines == None:
            # workaround to initialize Inlines collection
            tb.Text = ''
        tb.Inlines.Clear()
        tb.Inlines.Add(run)
        return tb
        
    def TryExpression(self, text):            
        props = self.engine.CreateScriptSourceFromString(text, SourceCodeKind.Expression).GetCodeProperties()
        if (props == SourceCodeProperties.None) or (props == SourceCodeProperties.IsEmpty):
            result = text
        else:
            result = None
        #print "TryExpression: %s" % (result != None)
        return result
    
    def FormatResult(self, obj):
        return repr(obj)
    
class JSEngineHelper(LanguageEngine):
    def TryExpression(self, text):
        if text.startswith('function '): return False
        return LanguageEngine.TryExpression(self, text)

class RubyEngineHelper(LanguageEngine):
    def __init__(self, *args):
        LanguageEngine.__init__(self, *args)
        self.engine.Compile(self.engine.CreateScriptSourceFromString('''
        require 'IronPython'
        
        # Redirect stdout/stderr to Python's print statement
        # necessary for now because IronRuby doesn't support redirecting IO
        class IO
            def write(x)
                ::IronPython::Runtime::Operations::PythonOps.PrintNoNewline(String === x ? x.to_clr_string : x)
            end
        end
        
        # Inject "canvas"
        $canvas = ::System::Windows::Application.Current.RootVisual.FindName('xaml_root_canvas')
        def canvas
            $canvas
        end
        ''', SourceCodeKind.File)).Execute(scriptEnv.CreateScope())
    
    # Format the reslt using Ruby's inspect & not Python's repr
    def FormatResult(self, obj):
        clr.AddReference('IronRuby', 'IronRuby.Libraries')
        import Ruby
        sym = Microsoft.Scripting.SymbolTable.StringToId('last_result')
        Ruby.IronRuby.GetExecutionContext(self.engine).GlobalVariables[sym] = obj
        result = self.engine.Execute(scriptEnv.CreateScope(), self.engine.CreateScriptSourceFromString('$last_result.inspect.to_clr_string'))
        #print 'FormatResult(%r) => %r' % (obj, result)
        return result
        
def GetEngine(name):
    if name == 'Python':
        engine = scriptEnv.GetEngine("py")
        return LanguageEngine(engine, py_pre, py_code_test, name, "py", wpf.Color.FromArgb(0xFF, 0, 155, 0))
    elif name == 'JScript':
        engine = scriptEnv.GetEngine("js")
        return JSEngineHelper(engine, js_pre, js_code_test, name, "js", wpf.Color.FromArgb(0xFF, 100, 0, 155))
    elif name == 'Ruby':
        engine = scriptEnv.GetEngine("rb")
        return RubyEngineHelper(engine, rb_pre, rb_code_test, name, "rb", wpf.Color.FromArgb(0xFF, 185, 0, 0))
    
def write(text):
    print text
    
def GetAbsolutePosition(o):
    x, y = wpf.GetPosition(o)
    while o.Parent is not None:
        xp, yp = wpf.GetPosition(o.Parent)
        x += xp
        y += yp
        o = o.Parent
    return x,y
    
def AddNames(o, child):
    if child.Name:
        setattr(o, child.Name, child)
    if hasattr(child, 'Children'):
        for c in child.Children:
            AddNames(o, c)

class DLRConsoleApp:
    def __init__(self, root):
        self.root = root
        
        root.Background = SolidColorBrush(Colors.Red)
        
        # delayed loading here would feel much more interactive
        self.Initialize()
        
        Application.Current.Host.Content.Resized += self.OnResize
        
    def ReloadXaml(self):
        text = self.xamlEditor.GetText()
        self.canvas.Children.Clear()
        self.canvas.Background = SolidColorBrush(Colors.LightGray)
        try:
            newCanvas = XamlReader.Load(text)
        except Exception, e:
            #TODO need an error console to write this error to
            newCanvas = Canvas()
            tb = TextBlock()
            tb.Text = "Exception while loading xaml!\n" + e.ToString()
            newCanvas.Children.Add(tb)
            
            
        self.canvas.Children.Add(newCanvas)
        AddNames(self.canvas, newCanvas)
        
    def ExecuteCode(self):
        text = self.codeEditor.GetText()
        self.ReloadXaml()
        try:            
            self.codeEditor.Engine.Compile(self.codeEditor.Engine.CreateScriptSourceFromString(text, SourceCodeKind.File)).Execute(self.console.input.CurrentScope)
        except Exception, e:
            self.console.input.HandleException(e)
            self.tab.selected = 0
            self.Resize()
        
    def Execute(self, editor):
        if editor is self.xamlEditor:
            self.ReloadXaml()
        elif editor is self.codeEditor:
            self.ExecuteCode()
         
        
        
    def Initialize(self):
        self.KeyHandler = TextInputHandler(self.root)
        
        canvas = Pane()
        canvas.SetValue(FrameworkElement.NameProperty, "xaml_root_canvas")
        self.canvas = canvas
        
        console = ConsoleWindow(self, lang)
        
        console.HeaderName = "Console"
        self.console = console
        self.console.input.Colorize=False
       
        xamlEditor = Editor(self, lang)
        xamlEditor.HeaderName = "XAML"
        xamlEditor.Colorize = False
        self.xamlEditor = xamlEditor
        
        
        codeEditor = Editor(self, lang)
        codeEditor.HeaderName = "Code"
        self.codeEditor = codeEditor
        
        aboutBox = AboutBox(self)
        aboutBox.HeaderName = "About"
        self.aboutBox = aboutBox
        
        
        tab = TabPanel([console, xamlEditor, codeEditor, aboutBox])
        if demoMode:
            tab.selected=1
        self.tab = tab
        
        
        #console = codeEditor #Editor(self, lang) #Console(self, lang)
        langInfo = LanguageInfo(console.input, codeEditor)
        langInfo.SetFixedHeight(150)
        pane2 = StackPanel(True, [tab, langInfo])
        
        canvas.Background = SolidColorBrush(Colors.LightGray)
        langInfo.Background = SolidColorBrush(Colors.Black)
        
        self.pane = DividedStackPanel(False, [pane2, canvas])        
        self.root.Children.Add(self.pane)
        
        self.Resize()
        
        xamlEditor.AddInput(xaml_test)
        
        self.ReloadXaml()
        
        self.StartConsole(console.input, canvas, langInfo)
       
        
    def StartConsole(self, console, canvas, langInfo):    
        sys.stdout = console.console
        console.CurrentScope.SetVariable("canvas", canvas)
        console.CurrentScope.SetVariable("wpf", wpf)
        console.CurrentScope.SetVariable("write", write) 
            
        langInfo.SetLanguage(lang)   
        
        console.WriteHeader()
        if autoTest:
            for line in console.le.test.split('\n'):
                console.AddInput(line)
                console.DoInput()
                
            console.AddInput("2+22")
            console.Backspace()
            
            console.Caret.Move(-1)
            console.AddInput("3")
            console.Backspace()
            
        #self.TestColorize(console)
            
    def TestColorize(self, console):
        code = "for i = 1 to 30" #self.GetText()

        su = SourceUnit.CreateSnippet(console.input.CurrentEngine, code)
        tc = console.input.TokenCategorizer
        tc.Initialize(None, su.GetReader(), SourceLocation(0,1,1))
        tokens = tc.ReadTokens(len(code))
        lastIndex = 0
        for token in tokens:
            col0 = token.SourceSpan.Start.Column-1
            col1 = token.SourceSpan.End.Column-1
            print col0, col1

        
    def OnResize(self, *args):
        self.Resize()
        
    def Resize(self):
        self.root.Width = max(200, Application.Current.Host.Content.ActualWidth)
        self.root.Height = max(200, Application.Current.Host.Content.ActualHeight)
        
        self.pane.Resize(self.root.Width, self.root.Height)
   
   
class Pane(Canvas):
    def __init__(self, clip=True):
        if clip:
            self.Clip = RectangleGeometry()
        self.minimumHeight = 0
        self.maximumHeight = None
        self.minimumWidth = 0
        self.maximumWidth = None
        
    def SetFixedHeight(self, h):
        self.minimumHeight = self.maximumHeight = h
        
    def SetFixedWidth(self, w):
        self.minimumWidth = self.maximumWidth = w
          
    def Resize(self, width, height):
        self.Width = width
        self.Height = height
        if self.Clip:
            self.Clip.Rect = Rect(0, 0, self.Width, self.Height)
        return True
        
    def Focus(self):
        pass
        
class NameHelperPane(Pane):
    def __getattr__(self, name):
        return self.FindName(name)
        
def MakePoints(p):
    #replace with with code to generate a real Polygon when PointsCollection bug is fixed
    ret = Canvas()
    thickness = 2
    x0, y0 = pts[-1]
    for x,y in pts:
        line = Line()
        line.X1 = x0
        line.Y1 = y0
        line.X2 = x
        line.Y2 = y
        line.Stroke = SolidColorBrush(Colors.Black)
        line.StrokeThickness = thickness
        ret.Children.Add(line)
    
        x0, y0 = x, y
        
    return ret
        
class TabPanel(Pane):
    def __init__(self, children):
        Pane.__init__(self, False)
        self.children = children
        self.selected = 0
        self.border = 2
        self.header_height = 20
        
        fontSize = 12
        if demoMode: fontSize = 15
        self.header_height = fontSize + self.border*2 + 1
        
        
        b = Rectangle()
        b.Fill = SolidColorBrush(Colors.White)
        self.Children.Add(b)     
        self.headerHighlight = b
        
        self.headers = []
        sep = 8
        x = 2+sep
        y = 2
        for child in children:
            tb = TextBlock()
            tb.Text = child.HeaderName
            tb.FontSize = fontSize
            tb.Opacity = 0.5
            SetPosition(tb, x, y)
            x += tb.ActualWidth + 2*sep
            tb.MouseLeftButtonDown += self.SelectTab
            self.headers.append(tb)
            self.Children.Add(tb)
            
            l = Line()
            l.X1 = l.X2 = x-sep
            l.Y1 = 0
            l.Y2 = self.header_height+self.border
            l.Stroke = SolidColorBrush(Colors.DarkGray)
            l.StrokeThickness = 1
            self.Children.Add(l)
        
        self.chosen = None
        
        self.Background = SolidColorBrush(Colors.LightGray)
        self.outline = None
        
    def SelectTab(self, sender, args):
        for i, header in enumerate(self.headers):
            if header is sender:
                self.Select(i)
                return
        
    def MakeOutline(self):
        header = self.headers[self.selected]
        x1, y0 = wpf.GetPosition(header)
        x1 -= 8
        x2 = x1 + header.ActualWidth+8*2
        y1 = y0 + self.header_height
        y2 = self.Height-1
        x0 = 1
        x3 = self.Width-1
        
        pts = [(x0, y1), (x1, y1), (x1, y0), (x2, y0), (x2, y1), (x3, y1), (x3, y2), (x0, y2)]
        
        if self.outline is not None:
            self.Children.Remove(self.outline)
            
        self.outline = MakePolygon(pts)
        
        self.Children.Add(self.outline)
        
    def Resize(self, width, height):
        width = max(self.border*3, width)
        height = max(self.header_height+self.border*2, height)
    
        Pane.Resize(self, width, height)
        
        #self.MakeOutline()
        self.Select(self.selected)
        
    def Select(self, index):
        self.selected = index
        if self.chosen:
            self.Children.Remove(self.chosen)
        
        chosen = self.children[self.selected]
        self.chosen = chosen
        chosen.Resize(self.Width-self.border*2, self.Height-(self.header_height+self.border*2))
        SetPosition(chosen, self.border, self.header_height+self.border)
        
        for header in self.headers:
            header.Opacity = 0.5
        
        chosenHeader = self.headers[index]
        chosenHeader.Opacity = 1.0
        self.headerHighlight.Width = chosenHeader.ActualWidth + 8*2
        self.headerHighlight.Height = self.header_height
        
        x, y = GetPosition(chosenHeader)
        SetPosition(self.headerHighlight, x-8, self.border) 
        
        self.Children.Add(chosen)
        chosen.Focus()
        
        

class StackPanel(Pane):
    def __init__(self, vertical, children):
        Pane.__init__(self, False)
        self.vertical = vertical
        
        for child in children:
            self.Children.Add(child)
        
    def GetSize(self, control):
        if self.vertical: return control.Height
        else: return control.Width
        
            
    def Resize(self, width, height):
        Pane.Resize(self, width, height)
        
        size = self.GetSize(self)
        minSize = 0
        for child in self.Children:
            minSize += child.minimumHeight
        y = 0
        if minSize >= size:
            size = minSize
            self.Height = size
            for child in self.Children:
                child.Resize(self.Width, child.minimumHeight)
                SetPosition(child, 0, y)
                y += child.Height
            return False
        
        maxSize = 0
        minSize = 0
        fillChildren = []
        for child in self.Children:
            if child.maximumHeight is None:
                fillChildren.append(child)
            else:
                maxSize += child.maximumHeight
                minSize += child.minimumHeight
                
        if maxSize < size:
            sz = (size-maxSize)//len(fillChildren)
            for child in self.Children:
                if child.maximumHeight is None:
                    child.Resize(self.Width, sz)
                else:
                    child.Resize(self.Width, child.maximumHeight)
                SetPosition(child, 0, y)
                y += child.Height
            return True
            
        # otherwise we set everything to minimumHeight instead.
        sz = (size-minSize)//len(fillChildren)
        for child in self.Children:
            if child.maximumHeight is None:
                child.Resize(self.Width, sz)
            else:
                child.Resize(self.Width, child.minimumHeight)
            SetPosition(child, 0, y)
            y += child.Height
        return True


class DividedStackPanel(Pane):
    def __init__(self, vertical, children):
        Pane.__init__(self, False)
        self.vertical = vertical
        self.dividerSize = 4
        
        inputPercents = 0.0
        unknownPercents = 0
        for child in children:
            if not hasattr(child, 'stackPercentage'):
                child.stackPercentage = None
                unknownPercents += 1
            else:
                inputPercents += child.stackPercentage
                
        if unknownPercents > 0:
            perc = (1.0-inputPercents)/unknownPercents
            
            for child in children:
                if not child.stackPercentage:
                    child.stackPercentage = perc
                    
                    
        self.selectedDivider = None
                    
        self.Children.Add(children[0])
        for child in children[1:]:
            self.Children.Add(self.MakeDivider())
            self.Children.Add(child)
            
        self.MouseLeftButtonUp += self.CancelDragging
        self.MouseLeave += self.CancelDragging
        self.MouseMove += self.DoDrag
            
    def MakeDivider(self):
        divider = Rectangle()
        divider.Height = self.dividerSize
        divider.Width = self.dividerSize
        divider.Cursor = Cursors.Hand
        divider.Fill = SolidColorBrush(Colors.DarkGray)
        divider.MouseLeftButtonDown += self.OnClick
        return divider
        
    def CancelDragging(self, *args):
        self.selectedDivider = None
        
    def DoDrag(self, sender, me):
        if self.selectedDivider is None: return
        
        divider = self.Children[self.selectedDivider]
        left = self.Children[self.selectedDivider-1]
        right = self.Children[self.selectedDivider+1]

        delta = me.GetPosition(divider).X-(divider.Width//2)
        
        dpos = GetPosition(divider)[0]+delta
        if dpos < 0:
            dpos = 0
            delta = dpos-GetPosition(divider)[0]
        if dpos + divider.Width >= self.Width:
            dpos = self.Width-divider.Width-1
            delta = dpos-GetPosition(divider)[0]
        
        left.Resize(left.Width + delta, left.Height)
        right.Resize(right.Width - delta, right.Height)
        SetPosition(right, GetPosition(right)[0]+delta, 0)
        SetPosition(divider, dpos, 0)
        
        
    def OnClick(self, sender, args):
        self.selectedDivider = self.Children.IndexOf(sender)
            
    def Resize(self, width, height):
        Pane.Resize(self, width, height)
        availableWidth = width - ((self.Children.Count//2)*self.dividerSize)
        if availableWidth < 0: availableWidth = 0
        
        i = 0
        x = 0
        while i < self.Children.Count-1:                   
            child = self.Children[i]
            i += 1
            child.Resize(availableWidth * child.stackPercentage, height)
            SetPosition(child, x, 0)
            x += child.Width
        
            divider = self.Children[i]
            i += 1
            divider.Height = height
            SetPosition(divider, x, 0)
            x += divider.Width
            
        child = self.Children[i]
        child.Resize(width-x+1, height)
        SetPosition(child, x, 0)

class ScrollBar(Canvas):
    def __init__(self, percent, top):
        r = Rectangle()
        r.Fill = SolidColorBrush(Colors.White)
        r.Stroke = SolidColorBrush(Colors.Black)
        self.Outline = r
        self.Children.Add(self.Outline)
        
        r = Rectangle()
        r.Fill = SolidColorBrush(Colors.LightGray)
        self.Body = r
        self.Children.Add(self.Body)
        
        self.percent = percent
        self.top = top
        
        self.Body.MouseLeftButtonDown += self.StartDragging
        self.Body.MouseEnter += self.Highlight
        self.Body.MouseLeave += self.RemoveHighlight
        
        self.Cursor = Cursors.Arrow
        self.dragging = False
        
        self.listeners = []
        
    def Highlight(self, s, e):
        self.Body.Fill = wpf.DarkGrayBrush
    
    def RemoveHighlight(self, s, e):
        if not self.dragging:
            self.Body.Fill = SolidColorBrush(Colors.LightGray)
        
    def StartDragging(self, s, e):
        root.MouseLeftButtonUp += self.StopDragging
        root.MouseLeave += self.StopDragging
        
        self.y_offset = e.GetPosition(self).Y-(self.top*(self.Outline.Height-2)+1)
        self.dragging = True
        root.MouseMove += self.Drag
        
    def StopDragging(self, s, e):
        self.dragging = False
        self.RemoveHighlight(s, e)
        root.MouseLeftButtonUp -= self.StopDragging
        root.MouseLeave -= self.StopDragging
        root.MouseMove -= self.Drag
    
    def Drag(self, s, e):
        new_y = e.GetPosition(self).Y
        
        y_top = new_y - self.y_offset
        self.top = (y_top-1)/float(self.Outline.Height-2)
        
        #self.top += (new_y-self.start_y)/float(self.Outline.Height-2)
        
        self.top = max(0, self.top)
        self.top = min(self.top, 1.0-self.percent)
        
        self.Resize(self.Outline.Width, self.Outline.Height)
        
        for listener in self.listeners:
            listener.UpdateScroll(self)
        
    def Resize(self, width, height):
        self.Outline.Width = width
        self.Outline.Height = height
        
        self.Body.Width = width-2
        self.Body.Height = (height-1) * self.percent
        
        y = self.top*(height-2)+1
        
        wpf.SetPosition(self.Body, 1, y)

class AboutBox(Pane):
    def __init__(self, app):
        Pane.__init__(self)
        self.Background = wpf.SolidColorBrush(Colors.White)
        
        text = TextBlock()
        text.Text = aboutText
        text.FontSize = 12
        text.TextWrapping = wpf.TextWrapping.Wrap
        text.Foreground = wpf.SolidColorBrush(Colors.Black)
        
        self.Children.Add(text)
        self.text = text
        
        link = TextBlock()
        link.Text = linkText
        link.FontSize = 16
        link.Foreground = wpf.SolidColorBrush(Colors.Blue)
        link.Cursor = Cursors.Hand
        link.MouseLeftButtonDown += self.OnClick
        
        self.Children.Add(link)
        
        self.link = link
        
    def Resize(self, width, height):
        Pane.Resize(self, width, height)
        
        self.text.Width = width
        
        x = max(0, (width-self.link.ActualWidth)/2)
        
        wpf.SetPosition(self.link, x, self.text.ActualHeight + 5)
        
    def OnClick(self, s, e):
        HtmlPage.Window.Navigate(Uri(s.Text, UriKind.Absolute))


class LanguageInfo(Pane):
    def __init__(self, console, codeEditor):
        Pane.__init__(self)
        self.console = console
        self.codeEditor = codeEditor
        suggestion = TextBlock()
        suggestion.Foreground = SolidColorBrush(Colors.White)
        suggestion.FontSize = 10
        SetPosition(suggestion, 230, 2)
        self.Children.Add(suggestion)
        self.suggestion = suggestion
        
        self.labels = []
        top = 5
        for name in languages:
            label = self.MakeLabel(name, top)
            self.labels.append(label)
            self.Children.Add(label)
            top += 40
            
    def OnClick(self, sender, args):
        self.SetLanguage(sender.Text)
        
    def SetLanguage(self, lang):
        self.console.SetLanguage(lang)
        self.suggestion.Text = self.console.le.test
        self.codeEditor.ClearText()
        self.codeEditor.AddInput(self.console.le.code_test)
        self.codeEditor.SetLanguage(lang)
        for label in self.labels:
            if label.Text == lang:
                label.Foreground = SolidColorBrush(Colors.White)
            else:
                label.Foreground = SolidColorBrush(Colors.DarkGray)
            
    def MakeLabel(self, lang, top):
        label = TextBlock()
        label.Text = lang
        label.Foreground = SolidColorBrush(Colors.Black)
        label.FontSize = 28
        SetPosition(label, 5, top)
            
        label.MouseLeftButtonDown += self.OnClick
        
        return label    


class KeyPress:
    def __init__(self, name, text):
        self.Text = text
        self.Name = name

class TextInputHandler:
    def __init__(self, root):
        keys = {}
        keys[0] = 'KeyNone'
        keys[1] = 'Backspace'
        keys[2] = '\t', '\t'
        keys[3] = '\n', '\n'
        keys[4] = 'Shift'
        keys[5] = 'Ctrl'
        keys[6] = 'Alt'
        keys[7] = 'CapsLock'
        keys[8] = 'Escape'
        keys[9] = ' ', ' '
        keys[10] = 'PageUp'
        keys[11] = 'PageDown'
        keys[12] = 'End'
        keys[13] = 'Home'
        
        keys[14] = 'Left'
        keys[15] = 'Up'
        keys[16] = 'Right'
        keys[17] = 'Down'
        keys[18] = 'Delete'
        keys[19] = 'Insert'

        
        #numbers
        keys[20] = '0', ')'
        keys[21] = '1', '!'
        keys[22] = '2', '@'
        keys[23] = '3', '#'
        keys[24] = '4', '$'
        keys[25] = '5', '%'
        keys[26] = '6', '^'
        keys[27] = '7', '&'
        keys[28] = '8', '*'
        keys[29] = '9', '('
        #letters
        for i in range(30, 56): keys[i] = chr(i+67), chr(i+35)
        
        #function keys
        for i in range(56, 68): keys[i] = 'F'+str(i-55)
        
        #numpad numbers
        for i in range(68, 78): keys[i] = str(i-68), str(i-68)
        
        keys[78] = '*', '*'
        keys[79] = '+', '+'
        keys[80] = '-', '-'
        keys[81] = '.', '.'
        keys[82] = '/', '/'

        #Treat numeric keypad as never num locked?
        #keys[69] = 'End'
        #keys[70] = 'Down'
        #keys[71] = 'PageDown'
        #keys[72] = 'Left'
        #keys[73] = 'Center'
        #keys[74] = 'Right'
        #keys[75] = 'Home'
        #keys[76] = 'Up'
        #keys[77] = 'PageUp'
        #keys[68] = 'Insert'
        #keys[81] = 'Delete'
        
        self.keys = keys
        
        self.pkeys = self.make_pkeys()
        
        self.Target = None
        
        root.KeyDown += self.OnKeyDown
        
    def make_pkeys(self):    
        # we don't need to detect platform as the platformKeyCodes that
        # we're interested appear to be distinct sets on windows and mac
        # with no overlap so we just support both of them.
        #
        # windows_code = mac_code = values
        pkeys = {}  
        pkeys[186] = pkeys[41] = ';',':'
        pkeys[187] = pkeys[24] = '=','+'
        pkeys[188] = pkeys[43] = ',', '<'
        pkeys[189] = pkeys[27] = '-', '_'
        pkeys[190] = pkeys[47] = '.', '>'
        pkeys[191] = pkeys[44] = '/', '?'
        pkeys[192] = pkeys[50] = '`', '~'
        
        pkeys[219] = pkeys[33] = '[', '{'
        pkeys[220] = pkeys[42] = '\\', '|'
        pkeys[221] = pkeys[30] = ']', '}'
        pkeys[222] = pkeys[39] = "'", '"'
        
        #Windows only for today
        pkeys[144] = 'NumLock'      
        pkeys[91] = 'Windows'  
        
        # this is a jolt bug
        pkeys[46] = 'Delete' #!!! need mac version
        #these two seem only needed for firefox on windows
        pkeys[61] = '=','+'
        pkeys[59] = ';',':'
        
        #This seems to turn up randomly
        pkeys[0] = 'Unknown'
        
        return pkeys
        
    def MakePressHelper(self, k, keyEventArgs):
        shiftkey = (Keyboard.Modifiers.value__ & ModifierKeys.Shift.value__) != 0
        ctrlkey = (Keyboard.Modifiers.value__ & ModifierKeys.Control.value__) != 0
        if type(k) is tuple:
            ret = KeyPress(k[0], k[shiftkey])
        else:
            ret = KeyPress(k, None)
        ret.Shift = shiftkey
        ret.Ctrl = ctrlkey
        return ret
        
    def MakeKeyPress(self, keyEventArgs):
        key = keyEventArgs.Key.value__
        if key != 255:
            if self.keys.has_key(key):
                return self.MakePressHelper(self.keys[key], keyEventArgs)
            return None
        pkey = keyEventArgs.PlatformKeyCode
        if self.pkeys.has_key(pkey):
            return self.MakePressHelper(self.pkeys[pkey], keyEventArgs)
        return None
        
    def OnKeyDown(self, sender, keyEventArgs):
        key = self.MakeKeyPress(keyEventArgs)
        if key is None:
            #let's be very noisy about ignoring unrecognized keys...
            print 'unrecognized key!', keyEventArgs.Key, keyEventArgs.PlatformKeyCode
            return
            
        #don't return control keys - make this configurable
        if key.Name in ['Shift', 'Ctrl', 'Alt']:
            return
            
        if self.Target is not None:
            self.Target.HandleKey(key)
    
class CompletionList:
    def __init__(self, console):
        self.console = console
        # assumptions embedded here that console is always in top/left position
        self.canvas = root
        if demoMode:
            self.fontSize = 16
            self.spacing = 18
        else:
            self.fontSize = 10
            self.spacing = 12
        self.box = None
        self.input = ""
        
    def IsVisible(self):
        return self.box is not None
        
    def Make(self, items):
        box = Canvas()
        self.box = box
        
        rect = Rectangle()
        rect.Stroke = SolidColorBrush(Colors.Black)
        rect.StrokeThickness = 1
        rect.Fill = SolidColorBrush(Colors.White)
        box.Children.Add(rect)
        
        select = Rectangle()
        select.Fill = SolidColorBrush(Colors.LightGray)
        box.Children.Add(select)
        self.select = select
        
        entries = []
        width = 40
        for item in items:
            tb = self.MakeEntry(item)
            width = max(width, tb.ActualWidth)
            entries.append(tb)
        self.entries = entries
        
        n = min(8, len(entries))

        
        rect.Width = 4 + width
        select.Width = width
        select.Height = self.spacing-1
        rect.Height = self.spacing*n + 4
        
        self.topIndex = 0
        self.visibleEntries = n
        self.selectedIndex = 0
        self.UpdateView()

        return box
        
    def ClearEntries(self):
        for i in range(self.box.Children.Count-1, 1, -1):
            self.box.Children.RemoveAt(i)
            
        
    def UpdateView(self):
        self.ClearEntries()
        top = 1
        for entry in self.entries[self.topIndex:self.topIndex+self.visibleEntries]:
            SetPosition(entry, 4, top)
            self.box.Children.Add(entry)
            top += self.spacing
            
        offset = self.selectedIndex - self.topIndex
        SetPosition(self.select, 2, 2 + offset*self.spacing)
        
        
    def SetSelection(self, index):
        self.selectedIndex = min(max(index, 0), len(self.entries)-1)
        if self.selectedIndex < self.topIndex:
            self.topIndex = self.selectedIndex
            
        below = self.topIndex + self.visibleEntries - 1 - self.selectedIndex
        if below < 0:
            self.topIndex -= below
        self.UpdateView()
        
    def MoveSelection(self, offset):
        self.SetSelection(self.selectedIndex + offset)
        
    def FindSelection(self, text):
        ltext = text.lower()
        for index in range(len(self.entries)):
            le = self.entries[index].Text.lower()
            if le.startswith(ltext):
                self.SetSelection(index)
                return
            
    def MakeEntry(self, item):
        tb = TextBlock()
        tb.FontSize = self.fontSize
        tb.Text = item
        return tb
        
    def Show(self, items, position):
        items = [i for i in items if '_' not in i]
        items.sort(cmp, str.lower)
        
        self.Make(items)
        
        cx, cy = GetAbsolutePosition(self.console)
        x, y = position
        x += cx
        y += cy
        y += self.console.spacing
        SetPosition(self.box, x, y)
        
        self.canvas.Children.Add(self.box)
        
        self.console.app.KeyHandler.Target = self
        
    def Hide(self):
        self.canvas.Children.Remove(self.box)
        self.input = ""
        self.box = None
        self.console.Focus()
        
    def FinishSelection(self):
        if self.input: self.console.Backspace(len(self.input))
        self.console.AddInput(self.entries[self.selectedIndex].Text)
        self.Hide()
        
    def HandleKey(self, key):
        if key.Text == '\n':
            self.FinishSelection()
            return
        elif key.Text is not None:
            if key.Text.isalnum():
                self.input += key.Text
                self.console.AddInput(key.Text)
                self.FindSelection(self.input)
                return
        elif key.Name == "Up":
            self.MoveSelection(-1)
            return
        elif key.Name == "Down":
            self.MoveSelection(+1)
            return
        elif key.Name == "Backspace":
            if self.input:
                self.input = self.input[:-1]
                self.console.Backspace()
                self.FindSelection(self.input)
                return
        self.Hide()
        self.console.HandleKey(key)

        
class CommandHistory:
    def __init__(self, console):
        self.history = []
        self.selectedIndex = None
        self.console = console
        
    def Add(self, command):
        if not command.strip(): return
        self.history.append(command)
        
    def SetSelection(self, index):
        self.ClearSelection()
        self.selectedIndex = min(max(index, 0), len(self.history)-1)
        self.ShowSelection()
        
    def ClearSelection(self):
        self.console.ClearText()
        
    def ShowSelection(self):
        text = self.history[self.selectedIndex]
        self.console.AddInput(text)
        self.console.console.Refresh()
        
    def Show(self):
        current = self.console.GetText()
        self.history.append(current)
        self.SetSelection(len(self.history)-2)
        self.console.app.KeyHandler.Target = self
        
    def Hide(self):
        self.history = self.history[:-1]
        self.selectedIndex = None
        self.console.Focus()
        
    def IsVisible(self):
        return self.selectedIndex is not None
        
    def HandleKey(self, key):
        if key.Name == 'Up':
            self.SetSelection(self.selectedIndex - 1)
        elif key.Name == 'Down':
            self.SetSelection(self.selectedIndex + 1)
        else:
            self.Hide()
            self.console.HandleKey(key)
        
        
cursor_template = """\
<Canvas
 xmlns="http://schemas.microsoft.com/client/2007"
 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
>
	<Canvas.Triggers>
		    <EventTrigger RoutedEvent='Canvas.Loaded'>
		      <EventTrigger.Actions>
				    <BeginStoryboard>
					    <Storyboard>
			<DoubleAnimation AutoReverse="True" From="1" To="0" By = "1" BeginTime="00:00:00" Duration="00:00:00.6" RepeatBehavior="Forever" Storyboard.TargetName="cursor_rectangle_%(name)s" Storyboard.TargetProperty="(UIElement.Opacity)">
			</DoubleAnimation>
		</Storyboard>
				    </BeginStoryboard>
		      </EventTrigger.Actions>
		    </EventTrigger>
        </Canvas.Triggers>

	<Rectangle Fill="#FF000000" x:Name="cursor_rectangle_%(name)s" Width="1" Height="%(height)s"/>
</Canvas>
"""
counter = 0
def MakeCursor(height):
    global counter
    ret = wpf.XamlReader.Load(cursor_template % {'name':str(counter), 'height':str(height)})
    counter += 1
    return ret
        
class Caret:
    def __init__(self, console):
        self.console = console
        self.line = 0
        self.column = 0
        
        self.toPosition = None
        
        self.visual = Canvas()
        
        cursor = MakeCursor(console.styles.GetSpacing())
        self.visual.Children.Add(cursor)
        self.cursor = cursor
        
    def GetLine(self):
        return self.console.lines[self.line]
        
    def GetPosition(self):
        return self.console.lines[self.line].GetPosition(self.column)
        
    def SetPosition(self, line, column):
        self.toPosition = None
        self.line = line
        self.column = column
        self.UpdatePosition()
        
    def FromScreenPosition(self, x, y):
        line = self.console.top_line + int(y/self.console.spacing)
        if line < 0: line = 0
        if line >= len(self.console.lines): line = len(self.console.lines)-1
        column = self.console.lines[line].GetColumnFromPosition(x)
        return line, column
        
    def SetFromScreenPosition(self, x, y):
        line, column = self.FromScreenPosition(x, y)
        self.SetPosition(line, column)
        
    def Move(self, offset):
        self.column += offset
        if self.column < 0:
            if self.line == 0:
                self.column = 0
                return False
            else:
                self.line -= 1
                self.column = self.GetLine().Columns()
        elif self.column > self.GetLine().Columns():
            if self.line >= len(self.console.lines)-1:
                self.column = self.GetLine().Columns()
                return False
            else:
                self.line += 1
                self.column = 0
        self.UpdatePosition()
        return True
        
    def MoveLine(self, offset):
        x, y = self.GetPosition()
        self.line += offset
        if self.line < 0:
            self.line = 0
        elif self.line >= len(self.console.lines):
            self.line = len(self.console.lines)-1
            
        self.column = self.GetLine().GetColumnFromPosition(x)
        self.UpdatePosition()
        
    def SelectToScreenPosition(self, x, y):
        line, column = self.FromScreenPosition(x, y)
        self.toPosition = None
        if line != self.line or column != self.column:                
            self.toPosition = line, column
            self.UpdatePosition()
    
    def DeleteSelection(self):
        if self.toPosition is None: return
        
        l0, c0 = self.line, self.column
        l1, c1 = self.toPosition
        
        if l1 < l0 or (l1 == l0 and c1 < c0):
            (l0, c0), (l1, c1) = (l1, c1), (l0, c0)
            self.line = l0
            self.column = c0
        
        if l0 == l1:
            self.console.lines[l0].DeleteRange(c0, c1)
        else:
            self.console.lines[l0].DeleteRange(c0, self.console.lines[l0].Columns())
            self.console.lines[l1].DeleteRange(0, c1)
            
            self.console.lines[l0].AddLine(self.console.lines[l1])
            
            self.console.lines = self.console.lines[:l0+1] + self.console.lines[l1+1:]
        
        self.toPosition = None
        self.UpdatePosition()
            
    def UpdatePosition(self):  
        self.visual.Children.Clear()
  
        if self.console.ShowCaret and self.line >= self.console.top_line and self.line < self.console.top_line + self.console.visible_lines:
            x, y = self.GetPosition()
            self.cursor.Opacity = 1.0
            SetPosition(self.cursor, x+1, y)
            self.visual.Children.Add(self.cursor)
        
        if self.toPosition is not None:
            self.visual.Children.Add(self.HighlightSelection( (self.line, self.column), self.toPosition))
        
    def HighlightSelection(self, (l0, c0), (l1, c1)):
        if l1 < l0 or (l1 == l0 and c1 < c0):
            (l0, c0), (l1, c1) = (l1, c1), (l0, c0)
    
        pg = wpf.Polygon()
        col = c0
        sep = self.console.spacing
        for li in range(l0, l1+1):
            line = self.console.lines[li]
            x, y = line.GetPosition(col)
            pg.Points.Add(wpf.Point(x, y))
            pg.Points.Add(wpf.Point(x, y+sep))
            col = 0
        
        col = c1
        for li in range(l1, l0-1, -1):
            line = self.console.lines[li]
            x, y = line.GetPosition(min(col, line.Columns()))
            x = max(int(sep/3), x)
            pg.Points.Add(wpf.Point(x, y+sep))
            pg.Points.Add(wpf.Point(x, y))
            col = 1000
            
        pg.Stroke = wpf.SolidColorBrush(Colors.Blue)
        
        gb = wpf.LinearGradientBrush()
        s0 = wpf.GradientStop()
        s0.Color = wpf.Color.FromArgb(0xFF, 235, 245, 255)
        s1 = wpf.GradientStop()
        s1.Color = wpf.Color.FromArgb(0xFF, 140, 200, 255)
        s1.Offset = 1.0
        
        gb.GradientStops.Add(s0)
        gb.GradientStops.Add(s1)
        #gb.EndPoint = wpf.Point(0.2, 1)
        
        pg.Fill = gb
        pg.SetValue(wpf.Canvas.ZIndexProperty, -1)
        
        return pg
        
class TextBuffer(Pane):
    def __init__(self, app):
        Pane.__init__(self)
        self.app = app
        
        self.styles = Styles()
        self.spacing = self.styles.GetSpacing()
        
        self.top_line = 0
        self.visible_lines = 10

        self.VerticalScroll = ScrollBar(1.0, 0.0)
        self.VerticalScroll.listeners.append(self)

        self.lines = []
        self.AddStyledLine("", None)
        
        self.Caret = Caret(self)
        self.Cursor = Cursors.IBeam
        self.ShowCaret = False
        self.ShowLastEmptyLine = False
        
        self.skip_redraw = False
        self.dragging = False
        self.MouseLeftButtonDown += self.OnClick
        
        self.MouseLeave += self.CancelDrag
        self.MouseLeftButtonUp += self.CancelDrag
        
        self.MouseMove += self.OnMouseMove
        
        self.Background = SolidColorBrush(Colors.White)
        
         
    def GetText(self):
        return '\n'.join([line.GetText() for line in self.lines])
        
    def GetDesiredHeight(self):
        return self.LastLine() * self.spacing + 3
        
    def OnClick(self, sender, mouseEventArgs):
        p = mouseEventArgs.GetPosition(self)
        self.Caret.SetFromScreenPosition(p.X, p.Y)
        self.dragging = True
        
    def CancelDrag(self, s, e):
        self.dragging = False
        
    def OnMouseMove(self, sender, mouseEventArgs):
        if self.dragging:
            p = mouseEventArgs.GetPosition(self)
            self.Caret.SelectToScreenPosition(p.X, p.Y)
        
    def Resize(self, width, height):
        Pane.Resize(self, width, height)
        self.visible_lines = int((self.Height-3)/self.spacing)
        self.ScrollLastLineVisible(True)
        
    def LastLine(self):
        if self.ShowLastEmptyLine or len(self.lines[-1].GetText().strip()) > 0:
            return len(self.lines)
        else:
            return len(self.lines)-1
        
    def ScrollLastLineVisible(self, force_redraw):
        new_top = max(0, self.LastLine()-self.visible_lines)
        if new_top != self.top_line or force_redraw:
            self.top_line = new_top
            self.UpdateTopLine()
    
    def UpdateTopLine(self):
        if self.skip_redraw: return
        
        self.Children.Clear()
        self.Children.Add(self.Caret.visual)
        
        y = 0
        for i in range(self.top_line, self.top_line+self.visible_lines):
            if i < len(self.lines):
                line = self.lines[i]
                self.Children.Add(line)
                SetPosition(line, 0, y)
                y += self.spacing
                
        self.Caret.UpdatePosition()
        
        if self.visible_lines < self.LastLine():
            self.VerticalScroll.percent = self.visible_lines/float(self.LastLine())
            self.VerticalScroll.top = self.top_line/float(self.LastLine())
            w = 15
            self.VerticalScroll.Resize(w, self.Height)
            wpf.SetPosition(self.VerticalScroll, self.Width-w+1, 0)
            self.Children.Add(self.VerticalScroll)
            
    def UpdateCaret(self):
        self.Caret.SetPosition(len(self.lines)-1, len(self.lines[-1].GetText()))

    def UpdateScroll(self, sb):
        self.Caret.toPosition = None
        self.top_line = int(self.LastLine()*sb.top)
        self.UpdateTopLine()

    def AddStyledLine(self, text, style):
        line = SimpleLine(self)
        if text:
            line.AddStyledText(text, style)
        self.lines.append(line)
            
    def GetStyle(self, kind):
        if isinstance(kind, ConsoleStyle): return kind
        return getattr(self.styles, kind)
        
    def ClearText(self):
        self.lines = self.lines[:1]
        self.lines[0].ClearText()
        self.UpdateCaret()
        self.UpdateTopLine()

    def write(self, text, kind='output'):
        style = self.GetStyle(kind)
        text = text.replace('\r\n', '\n')
        text = text.replace('\r', '\n')
        lines = text.split('\n')
        self.lines[-1].AddStyledText(lines[0], style)
        for line in lines[1:]:
            self.AddStyledLine(line, style)
        self.UpdateCaret()
        self.ScrollLastLineVisible(True)

    
class Editor(TextBuffer):
    def __init__(self, app, lang):
        TextBuffer.__init__(self, app)
        
        self.SetLanguage(lang)
        
        self.ShowCaret = True
        self.ShowLastEmptyLine = True
        self.Colorize = True
        
        self.CompletionList = CompletionList(self)
        
    def Focus(self):
        self.app.KeyHandler.Target = self
        
    def SetLanguage(self, name):
        self.le = GetEngine(name)
        self.Engine = self.CurrentEngine = self.le.engine
        self.TokenCategorizer = self.Engine.GetService[ITokenCategorizer]()
                
    def HandleKey(self, key):
        if key.Ctrl:
            if key.Text == '\n':
                self.app.Execute(self)
                return
        
        if key.Text == '\n':
            self.DoEnter()
            return
        elif key.Text is not None and not key.Ctrl:
            self.AddInput(key.Text)
        elif key.Name == 'Backspace':
            self.Backspace()
        if key.Name == 'Delete':
            self.Delete()
        elif key.Name == 'Up':
            self.Up()
        elif key.Name == 'Down':
            self.Down()
        elif key.Name == 'Left':
            self.Move(-1)
        elif key.Name == 'Right':
            self.Move(+1)

    def DoEnter(self):
        self.InsertText('\n')
        
    def InsertText(self, text):
        self.Caret.DeleteSelection()
        
        lines = text.split('\n')
        
        line = self.Caret.GetLine()
        line.InsertText(self.Caret.column, lines[0])
        
        if len(lines) > 1:
            start = self.Caret.column + len(lines[0])
            extra = line.GetText()[start:]
            line.DeleteRange(start, len(line.GetText()))
            
            for line in lines[1:-1]:
                self.Caret.line += 1
                self.AddLine(line, self.Caret.line)
            
            self.Caret.line += 1
            self.AddLine(lines[-1]+extra, self.Caret.line)
            self.Caret.column = len(lines[-1])
        else:
            self.Caret.column = self.Caret.column + len(lines[0])
        
        self.ScrollLastLineVisible(True)
       
    def Up(self):
        self.Caret.MoveLine(-1)

    def Down(self):
        self.Caret.MoveLine(+1)
        
    def Move(self, offset):
        return self.Caret.Move(offset)

    def Backspace(self, count=1):
        if self.Caret.toPosition is None:
            old_line = self.Caret.GetLine()
            self.Caret.toPosition = self.Caret.line, self.Caret.column
            if not self.Move(-count):
                self.Caret.toPosition = None
                return
        self.Caret.DeleteSelection()
        self.UpdateTopLine()
        
    def Delete(self, count=1):
        if self.Caret.toPosition is not None:
            self.Caret.DeleteSelection()
            self.UpdateTopLine()
            return
            
        line = self.Caret.GetLine()
        line.DeleteRange(self.Caret.column, self.Caret.column+count)
        #TODO delete end-of-line
        
    def AddInput(self, text):
        self.InsertText(text)
                
    def AddLine(self, text, index=None):
        line = CodeLine(self)
        if text:
            line.AddText(text)
        if index is None:
            self.lines.append(line)
        else:
            self.lines.insert(index, line)
            
    def AddStyledLine(self, text, style):
        self.AddLine(text)


class ConsoleWindow(Pane):
    def __init__(self, app, lang):
        Pane.__init__(self)
        
        self.output = TextBuffer(app)
        self.input = ConsoleInput(app, lang, self)
        
        self.Children.Add(self.output)
        self.Children.Add(self.input)
        
        self.Background = wpf.SolidColorBrush(wpf.Colors.White)
        #StackPanel.__init__(self, True, [self.output, self.input])
        
    def Resize(self, width, height):
        Pane.Resize(self, width, height)
        
        oh = self.output.GetDesiredHeight()
        ih = self.input.GetDesiredHeight()
        if oh + ih <= self.Height:
            iy = oh-3
        elif ih + self.output.spacing <= self.Height:
            iy = int((self.Height - ih - 3)/self.output.spacing)*self.output.spacing
        else:
            self.Height = ih + self.output.spacing
            iy = self.Height - ih
        
        self.output.Resize(width, iy+3)
        self.input.Resize(width, ih)
        wpf.SetPosition(self.input, 0, iy)
        
    def SetLanguage(self, name):
        self.input.SetLanguage(name)
        
    def Refresh(self):
        self.Resize(self.Width, self.Height)
        
    def write(self, text, kind='output'):
        self.output.write(text, kind)
        self.Refresh()

    def Focus(self):
        self.input.Focus()

class ConsoleInput(Editor):
    def __init__(self, app, lang, console):
        self.le = None
        self.Colorize = True
        Editor.__init__(self, app, lang)
        self.CommandHistory = CommandHistory(self)
        self.console = console
        self.Background = None
        
        self.InitializeModule()
    
    def SetGutters(self):
        if self.le is None: return
        for line in self.lines:
            gutter = self.MakePrompt(line is self.lines[0])
            line.SetGutter(gutter, gutter.ActualWidth)
        
    def HandleKey(self, key):
        if key.Text == '\n':
            self.DoInput(key.Ctrl)
        elif key.Text == '.':
            self.DoCompletions()
        elif key.Name == 'Up' and self.Caret.line == 0:
            self.DoHistory()
        else:
            Editor.HandleKey(self, key)
    
    def AddLine(self, text, index=None):
        Editor.AddLine(self, text, index)
        self.SetGutters()
        
    def SetLanguage(self, name):
        Editor.SetLanguage(self, name)
        self.SetGutters()
        for line in self.lines:
            line.Colorize()
    
        
    def DoCompletions(self):
        self.AddInput('.')
        HandleUserInputForCompletion(self, '.')
            
    def DoHistory(self):
        self.CommandHistory.Show()

    def InitializeModule(self):
        self.CurrentScope = scriptEnv.CreateScope()
        
    def WriteHeader(self):
        self.console.write(intro % {'language':self.le.longName}, "header")
        
    def Execute(self, text):
        self.Engine.Compile(self.Engine.CreateScriptSourceFromString(text)).Execute(self.CurrentScope)
        
    def HandleException(self, e):
        dfs = Microsoft.Scripting.RuntimeHelpers.GetDynamicStackFrames(e)
        if dfs is None or len(dfs) == 0:
            exc = self.Engine.FormatException(e)
            if not exc.endswith('\n'): exc += '\n'
            self.console.write(exc, 'error')
        else:
            self.console.write("%s: %s\n" % (e.GetType(), e.Message), "error")
            for frame in dfs[::-1]:
                self.console.write("  at %s in %s, line %s\n" %
                            (frame.GetMethodName(), frame.GetFileName(), 
                             frame.GetFileLineNumber()), "error")

    def DoInput(self, forceExecute=False):
        text = self.GetText()
        if len(text) == 0: return

        if len(self.lines) > 1:
            self.DoMultiLine(text, forceExecute)
        else:
            self.DoSingleLine(text, forceExecute)
            
        self.console.Refresh()
        self.SetGutters()
        
    def MakePrompt(self, initial):
        return self.le.MakePrompt(self.styles.prompt, initial)
            
    def WriteInputToConsole(self, text):
        if text.endswith('\n'): text = text[:-1]
        self.CommandHistory.Add(text)
        for line in self.lines:
            if len(line.GetText().strip()) == 0: continue
            #self.console.output.write(' ', self.styles.prompt)
            self.console.output.lines[-1].AddTextBlock(line.gutter)
            self.console.output.lines[-1].AddLine(line) 
            self.console.write('\n')
        self.ClearText()
            
    def DoSingleLine(self, text, forceExecute):        
        text1 = self.le.TryExpression(text)
        if text1:
            self.WriteInputToConsole(text)
            try:
                ret = self.Engine.Compile(self.Engine.CreateScriptSourceFromString(text)).Evaluate(self.CurrentScope)
                if ret is not None:
                    # give the language a chance to format its output
                    self.console.write(self.le.FormatResult(ret) + '\n', 'output')
                    self.CurrentScope.SetVariable('_', ret)
            except Exception, e:
                self.HandleException(e)
        else:            
            self.DoMultiLine(text, forceExecute)

    def DoMultiLine(self, text, forceExecute):
        allowIncomplete = len(self.lines[-1].GetText()) == 0
        if forceExecute or self.IsComplete(text, allowIncomplete):
            self.WriteInputToConsole(text)
            self.console.output.skip_redraw = True
            try:
                self.Engine.Compile(self.Engine.CreateScriptSourceFromString(text, SourceCodeKind.InteractiveCode)).Execute(self.CurrentScope)
            except Exception, e:
                self.HandleException(e)
            self.console.output.skip_redraw = False
            self.console.output.UpdateTopLine()
        else:
            self.AddInput('\n')

    def IsComplete(self, text, allowIncomplete):
        SCP = SourceCodeProperties
        props = self.Engine.CreateScriptSourceFromString(text, SourceCodeKind.InteractiveCode).GetCodeProperties()
        result = (props != SCP.IsInvalid) and (props != SCP.IsIncompleteToken) and (allowIncomplete or (props != SCP.IsIncompleteStatement))
        #print "IsComplete(%s): %s -> %s" % (allowIncomplete, props, result)
        return result
        
class SimpleLine(Canvas):
    def __init__(self, editor):
        self.textBlock = TextBlock()
        if self.textBlock.Inlines == None:
            # workaround to initialize Inlines collection
            self.textBlock.Text = ''
        self.textBlock.Inlines.Clear()
        self.Children.Add(self.textBlock)
        self.editor = editor
        self.gutterWidth = 0
        
    def SetGutter(self, gutter, width):
        self.gutterWidth = width
        while self.Children.Count > 1:
            self.Children.RemoveAt(1)
        self.Children.Add(gutter)
        wpf.SetPosition(self.textBlock, width, 0)
        self.gutter = gutter
        
    def GetText(self):
        return self.textBlock.Text
        
    def MakeFakeTextBlock(self):
        return self.editor.styles.input.MakeTextBlock()
            
    def GetPosition(self, column):
        top = self.GetValue(Canvas.TopProperty)
        if column == 0:
            return self.gutterWidth, top
        text = self.GetText()
        if column < len(text):
            tb = self.MakeFakeTextBlock()
            tb.Text = text[:column]
            return tb.ActualWidth + self.gutterWidth, top
        else:
            return self.textBlock.ActualWidth + self.gutterWidth, top
        
    def _MakeRun(self, style):      
        if self.textBlock.Inlines.Count > 0:
            run = self.textBlock.Inlines[self.textBlock.Inlines.Count-1]
            if style is None or style.Matches(run):
                return run

        run = style.MakeRun()
        self.textBlock.Inlines.Add(run)
        return run
        
    def ClearStyledText(self):
        self.textBlock.Inlines.Clear()
        
    def AddStyledText(self, text, style):
        run = self._MakeRun(style)
        run.Text += text
        
    def Columns(self):
        return len(self.GetText())
        
    def AddLine(self, line):
        self.AddTextBlock(line.textBlock)
            
    def AddTextBlock(self, tb):
        lines = list(tb.Inlines)
        tb.Inlines.Clear()
        for run in lines:
            self.textBlock.Inlines.Add(run)

        
    def GetColumnFromPosition(self, x):
        col = 0
        tb = self.MakeFakeTextBlock()
        last_cs = None
        for col, ch in enumerate(self.GetText()):
            cs = tb.ActualWidth + self.gutterWidth
            if cs == x:
                return col
            elif cs > x:
                if last_cs is not None:
                    d0 = abs(last_cs-x)
                    d1 = abs(cs-x)
                    if d0 < d1:
                        return col-1
                    else:
                        return col
                else:
                    return col
            tb.Text += ch
            last_cs = cs
            
            
        if x < self.textBlock.ActualWidth + self.gutterWidth:
            return col
        else:
            return col+1
            
class CodeLine(SimpleLine):
    def __init__(self, editor):
        SimpleLine.__init__(self, editor)
        self.text = ""
        
    def AddLine(self, line):
        self.text += line.GetText()
        self.Colorize()
        
    def DeleteToColumn(self, column):
        if column >= self.Columns():
            return
            
        self.text = self.text[:column]
        self.Colorize()
        
    def InsertText(self, column, text):
        self.text = self.text[:column] + text + self.text[column:]
        self.Colorize()
        
    def AddText(self, text):
        self.InsertText(self.Columns(), text)
        
    def DeleteRange(self, start, end):
        if start >= end: return
        
        self.text = self.text[:start] + self.text[end:]
        self.Colorize()
        
    def Columns(self):
        return len(self.text)

    def Backspace(self, count):
        n = self.Columns()
        self.DeleteRange(n-count, n)
        
    def ClearText(self):
        self.DeleteToColumn(0)
    
    def Colorize(self):
        if not self.editor.Colorize:
            self.ClearStyledText()
            self.AddStyledText(self.text, self.editor.styles.input)
            return
            
        state = None
        
        colors = {
          TokenCategory.Keyword : self.editor.styles.keyword,
          TokenCategory.StringLiteral: self.editor.styles.string,
          TokenCategory.Comment: self.editor.styles.comment,
          TokenCategory.LineComment: self.editor.styles.comment,
          TokenCategory.DocComment: self.editor.styles.comment,
        }
        defaultStyle = self.editor.styles.input
    
        code = self.text
        self.ClearStyledText()
        if len(code) == 0: return
        
        indent = 0
        while indent < len(code) and code[indent] == ' ':
            indent += 1
            
        code = code[indent:]

        su = self.editor.CurrentEngine.CreateScriptSourceFromString(code, SourceCodeKind.Statements)
        tc = self.editor.TokenCategorizer
        tc.Initialize(state, su.GetReader(), SourceLocation(0,1,1))
        try:
            tokens = tc.ReadTokens(len(code))
        except:
            self.AddStyledText(code, self.editor.styles.prompt)
            return
            
        if indent:
            self.AddStyledText(' '*indent, self.editor.styles.prompt)
            
        lastIndex = 0
        for token in tokens:
            col0 = max(0, token.SourceSpan.Start.Column-1)
            col1 = token.SourceSpan.End.Column-1
            if col0 > lastIndex:
                self.AddStyledText(code[lastIndex:col0], self.editor.styles.prompt)
            
            text = code[col0:col1]
            style = defaultStyle
            if colors.has_key(token.Category):
                style = colors[token.Category]
            self.AddStyledText(text, style)
            lastIndex = col1
        if lastIndex < len(code):
            self.AddStyledText(code[lastIndex:], self.editor.styles.prompt)
        
        return tc.CurrentState


class ConsoleStyle:
    def __init__(self, parent, color=None, size=None):
        if color is None:
            color = parent.color
        if size is None:
            size = parent.size

        self.color = color
        self.size = size

    def SetStyle(self, text):
        text.Foreground = SolidColorBrush(self.color)
        text.FontSize = self.size
        
    def MakeTextBlock(self):
        tb = TextBlock()
        tb.FontFamily = FontFamily("Courier New")
        self.SetStyle(tb)
        return tb
        
    def MakeRun(self):
        run = Run()
        run.FontFamily = FontFamily("Courier New")
        self.SetStyle(run)
        return run

    def Matches(self, text):
        return self.color == text.Foreground.GetValue(SolidColorBrush.ColorProperty)


class Styles:
    def __init__(self, size=13):
        if demoMode: size = 16
        self.size = size
        
        self.default = ConsoleStyle(None, color=Colors.Black, size=size)
        self.input = self.Make(color=Colors.Black)
        self.prompt = self.Make(color=Colors.LightGray)
        self.output = self.Make(color=Color.FromArgb(0xFF, 50, 0, 180))
        self.error = self.Make(color=Color.FromArgb(0xFF, 150, 0, 0))
        self.header = self.Make(color=Colors.Gray)
        
        self.comment = self.Make(color=Colors.Green)
        self.string = self.Make(color=Color.FromArgb(0xFF, 180, 0, 0))
        self.keyword = self.Make(color=Color.FromArgb(0xFF, 0, 0, 220))

    def GetSpacing(self):
        return self.size + 2

    def Make(self, color=None):
        return ConsoleStyle(self.default, color=color)




##################################################################################
#Nessie CodeSenseSupport
##################################################################################
def GetTokenTrigger(console, code):
    engine = console.CurrentEngine
    source_unit = engine.CreateScriptSourceFromString(code, SourceCodeKind.Statements)
    token_categorizer = console.TokenCategorizer
    token_categorizer.Initialize(None, source_unit.GetReader(), SourceLocation(0,1,1))
    return token_categorizer.ReadToken().Trigger

def HandleUserInputForMethodSignatureTip(console, args):
    if (len(args.NewText) == 1):
        if (Trigger(GetTokenTrigger(console, args.NewText), TokenTriggers.ParameterStart)):
            text = console.GetLineFromPosition(console.Caret.CurrentPosition.Index).GetText()
            text = text.Substring(0, len(text) - 1) # Remove the character that was just entered
            index_of_first_space = text.LastIndexOf(" ")
            if (index_of_first_space == -1):
                index_of_first_space = 0
            function = FindExpression(text.Substring(index_of_first_space).Trim(), console.CurrentEngine)
            try:
                function_signature = GetFunctionSignature(console, function)
                if (function_signature != None):
                    if (len(function_signature) > 0):
                        console.Tooltip.Show(function_signature, console.Caret.CurrentPosition.Index - 1)
            except Exception, e: 
                print e
        elif (Trigger(GetTokenTrigger(console, args.NewText), TokenTriggers.ParameterEnd)):
            console.Tooltip.Hide()
        else: pass
    else:
        console.Tooltip.Hide()
        
def HandleUserInputForCompletion(console, newText):
    if len(newText) == 1:
        if Trigger(GetTokenTrigger(console, newText), TokenTriggers.MemberSelect):
            text = console.Caret.GetLine().GetText()
            text = text[0: len(text) - 1] # Remove the character that was just entered
            index_of_first_space = text.LastIndexOf(" ")
            if (index_of_first_space == -1):
                index_of_first_space = 0
            obj = FindExpression(text.Substring(index_of_first_space).Trim(), console.CurrentEngine)
            members = GetMemberNames(console, obj)
            # TODO: len(members) is not working here in all cases            
            # but list comprehension trick seems to work
            if members is not None and len([i for i in members]) > 0:
                console.CompletionList.Show(members, console.Caret.GetPosition())

        else: pass
    else:
        console.CompletionList.Hide()

def GetFunctionSignature(console, function_name):
    obj = GetCurrentObject(console, function_name)
    if (obj != None):
        if (console.CurrentEngine.IsObjectCallable(obj, console.CurrentScope)):
            IObjectHandle = System.Runtime.Remoting.IObjectHandle
            # there may be a Python method dispatch bug here...
            get_object_doc_functions = console.CurrentEngine.GetObjectDocumentation
            get_object_doc = get_object_doc_functions.Overloads.GetOverload(IObjectHandle, get_object_doc_functions.Overloads.Targets)
            return get_object_doc(obj)
    
def GetMemberNames(console, name):
    # TODO: GetMemberNames throws on some pretty normal things like functions
    try:
        obj = GetCurrentObject(console, name)
        if (obj != None):
            return console.CurrentEngine.Operations.GetMemberNames(obj)
    except:
        pass
    return None

def GetCurrentObject(console, expression):
    engine = console.CurrentEngine
    source_unit = engine.CreateScriptSourceFromString(expression)
    token_categorizer = console.TokenCategorizer
    token_categorizer.Initialize(None, source_unit.GetReader(), SourceLocation(0,1,1))
    tokens = list(token_categorizer.ReadTokens(len(expression)))
    if tokens[0].Category == TokenCategory.Identifier:
        token = tokens[0]
        tokens = tokens[1: len(tokens)]
        current_object = console.CurrentEngine.TryGetVariable(console.CurrentScope, expression[0: token.SourceSpan.End.Index])
        if current_object[0]:
            current_object = current_object[1]
            while len(tokens) > 0:
                token = tokens[0]
                tokens = tokens[1: len(tokens)]
                token_text = expression[token.SourceSpan.Start.Index: token.SourceSpan.End.Index]
                if token.Category == TokenCategory.Identifier:
                    member_value = engine.TryGetObjectMemberValue(current_object, token_text, console.CurrentScope)
                    if member_value[0]:
                        current_object = member_value[1]
                    else:
                        return None
                elif token.Category == TokenCategory.EndOfStream:
                    return current_object
                elif token.Category == TokenCategory.WhiteSpace: # The Python token categorizer returns an extra whitespace token at the end of single line statements
                    pass
                elif Trigger(token.Trigger, TokenTriggers.MemberSelect):
                    pass
                else:
                    return None
            return current_object

def Trigger(t1, t2):
    return (t1.value__ == (t1.value__ | t2.value__))
    
def FindExpression(text, engine):
    index_of_first_space = text.LastIndexOf(" ")
    if (index_of_first_space == -1):
        index_of_first_space = 0
    text = text.Substring(index_of_first_space).Trim()

    source_unit = engine.CreateScriptSourceFromString(text)
    token_categorizer = engine.GetService[ITokenCategorizer]()
    token_categorizer.Initialize(None, source_unit.GetReader(), SourceLocation(0,1,1))
    tokens = token_categorizer.ReadTokens(len(text))
    token_list = []
    for token in tokens:
        token_list.append(token)
    token_list.reverse()
    if len(token_list) > 0:
        token = token_list[0]
        while((token_list[0].Trigger == TokenTriggers.None) | (Trigger(token_list[0].Trigger, TokenTriggers.MemberSelect))):
            token = token_list[0]
            token_list = token_list[1:]
            if (len(token_list) == 0):
                break
        
        return text.Substring(token.SourceSpan.Start.Index)
    return ""

root = Canvas()
Application.Current.RootVisual = root
app = DLRConsoleApp(root)
