import System
from System.Windows import *
from System.Windows.Browser import *
from System.Windows.Controls import *
from System.Windows.Documents import *
from System.Windows.Input import *
from System.Windows.Interop import *
from System.Windows.Markup import *
from System.Windows.Media import *
from System.Windows.Media.Animation import *
from System.Windows.Shapes import *

def SetPosition(o, x, y):
    # TODO: figure out why we're getting -1.#IND values passed in
    try:
        Canvas.SetTop(o, y)
    except: pass
    try:
        Canvas.SetLeft(o, x)
    except: pass
    
def GetPosition(o):
    return Canvas.GetLeft(o), Canvas.GetTop(o)
    
def __Setup():
	for name in dir(Colors):
		c = getattr(Colors, name)
		if isinstance(c, Color):
			bname = name+"Brush"
			globals()[bname] = SolidColorBrush(c)
	
__Setup()