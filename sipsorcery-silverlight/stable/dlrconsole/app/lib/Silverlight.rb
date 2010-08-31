include System::Windows
include System::Windows::Controls
include System::Windows::Media

class SilverlightApplication
  def method_missing(m)
    root.send(m)
  end
end

class FrameworkElement
  def method_missing(m)
    find_name(m.to_s.to_clr_string)
  end
end

