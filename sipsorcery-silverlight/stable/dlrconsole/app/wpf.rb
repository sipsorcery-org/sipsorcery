
include System::Windows
include System::Windows::Browser
include System::Windows::Controls
include System::Windows::Documents
include System::Windows::Input
include System::Windows::Interop
include System::Windows::Markup
include System::Windows::Media
include System::Windows::Media::Animation
include System::Windows::Shapes

class FrameworkElement
  def left
    Canvas.get_left self
  end
  def top
    Canvas.get_top self
  end
  def left=(x)
    Canvas.set_left(self, x)
  end
  def top=(y)
    Canvas.set_top(self, y)
  end 
end
