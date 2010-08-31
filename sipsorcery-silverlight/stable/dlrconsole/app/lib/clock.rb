include System::Windows
include System::Windows::Controls
include System::Windows::Media

class FrameworkElement
  def method_missing(m)
    find_name(m.to_s.to_clr_string)
  end
end

class Clock
  
  def initialize(canvas)
    @canvas = canvas
  end
  
  def load(xaml)
    Application.current.load_component canvas, "lib/#{xaml}"
  end
  
  def canvas
    @canvas
  end
  
  def left=(x)
    @canvas.left = x
  end
  def top=(y)
    @canvas.top = y
  end
  
  def set_hands(d)
    hour_animation.from    = from_angle  d.hour, 1, d.minute/2
    hour_animation.to      = to_angle    d.hour
    minute_animation.from  = from_angle  d.minute
    minute_animation.to    = to_angle    d.minute
    second_animation.from  = from_angle  d.second
    second_animation.to    = to_angle    d.second
  end

  def from_angle(time, divisor = 5, offset = 0)
    ((time / (12.0 * divisor)) * 360) + offset + 180
  end

  def to_angle(time)
    from_angle(time) + 360
  end
  
  def move(x,y)
    root.left, root.top = x, y
  end
  
  def method_missing(m)
    canvas.send(m)
  end
end
