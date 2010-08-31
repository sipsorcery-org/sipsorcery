Import("System");
Import("System.Windows");
Import("System.Windows.Application");
Import("System.Windows.Controls.UserControl");
Import("System.Windows.Browser");
Import("System.Windows.Media.*");
Import("System.Windows.Documents.*");
Import("System.Windows.Threading.*");

var agc;            	// agc object reference
var transformMatrix;
var counter = 0;        // Number of next text available i.e. text1, text2 , etc...
var objects;            // Holds information for all Texts on the Scene
var f;
var mouseX, mouseY;     // Mouse Position
var centerX, centerY;
var timerF = null;

var xaml = new UserControl

function load() {
	Application.Current.LoadComponent(xaml, '3dtext.xaml')
	xaml.Loaded += init
	//xaml.MouseMove += updateMousePosition
	//xaml.MouseEnter += onMouseEnter
}

function init(sender, args)
{
    agc = sender.Parent; 
    transformMatrix = new Array(new Array(1,0,0), new Array(0,1,0), new Array(0,0,1), new Array(0,0,0));
    objects = new Array();
    f = 300;
    centerX = 400;
    centerY = 300;
    mouseX = 0; mouseY = 0;

    //Initialize the product name array
    var textArray = [
    	"Windows", "Office", "XBOX 360", "Windows Live", 
		"Windows Vista", "Internet Explorer", "XBox Live", "Halo", "MSN", 
		"Media Center", "Mobile", "Dynamics", "Exchange Server", "Messenger", 
		"MSDN", "Office Live", "Zune", "DirectX", "Expression", "TechNet", 
		"Visual Studio", "Silverlight", "Playtable", "Virtual PC", "One Care", 
		"ASP.NET", "Visual C#", "Biztalk", "Gears of War", "MTV Urge", 
		"ActiveSync", "Outlook", "Word", "Excel", "SharePoint", "PowerPoint", 
		"Visio", "Publisher", "OneNote", "Live Meeting", "Office Live"
	]

    for (var i=0; i<textArray.length; i++)
    {
        var x = Math.random()*150 + 35;
        var y = Math.random()*140 + 35;
        var z = Math.random()*140 + 35;
        var neg = Math.random()*1;
        if (neg <= 0.5) x*= -1;
        var neg = Math.random()*1;
        if (neg <= 0.5) y*= -1;
        var neg = Math.random()*1;
        if (neg <= 0.5) z*= -1;

        create3DText(textArray[i], 20, [x,y,z], "Black", 1);
    }

	timerF = new DispatcherTimer();
	timerF.Tick += new System.EventHandler(refreshScene);
	timerF.Interval = System.TimeSpan.FromMilliseconds(30);
	timerF.Start();

    refreshScene();
}

function timertick(s,e)
{
		refreshScene();
}

//per frame render callback to update the scene
function AnimationCompleted(sender, eventArgs)
{
    refreshScene();
    //restart the per frame animation
    sender.Begin();
}

// -------------------------------
// Updates drawing
// -------------------------------
function refreshScene ()
{
    var y = -mouseY+centerY;
    var x = mouseX-centerX;
    if (y > 300) { y = 300; } else if (y < -300) { y = -300; }
    if (x > 400) { x = 400; } else if (x < -400) { x = -400; }
    x /= 2;
    y /= 2;
    setTransformMatrix(y , x, 0, transformMatrix);
    redraw();
}

//for initial load to trigger the animations
function onMouseEnter(sender, args)
{
    updateMousePosition(sender, args);
}

//Mouse move event handler
function updateMousePosition(sender, args)
{
    mouseX = args.GetPosition(null).X; // + document.body.scrollLeft;
    mouseY = args.GetPosition(null).Y; // + document.body.scrollTop;
    if (mouseX < 0) { mouseX = 0; }
    if (mouseY < 0) { mouseY = 0; }
    refreshScene();
}

function create3DText(productName, fontSize, point, color, opacity) 
{
    objects[counter] = new Object();
    objects[counter].point = point;
    objects[counter].color = color;
    objects[counter].opacity = opacity;

	var tempstr;
	
    tempstr = "objects[counter].text = xaml.text"+counter; 
    eval(tempstr);
    tempstr = "objects[counter].pos = xaml.t"+counter; 
    eval(tempstr);
    tempstr = "objects[counter].size = xaml.s"+counter;
    eval(tempstr);
	
    objects[counter].text.UnicodeString = productName; 
    objects[counter].text.FontRenderingEmSize = parseInt(fontSize);

    counter++;
}

function redraw()
{
	var i = 0;
    while (i < counter)
    {
        var o = objects[i];
        var point = mvm(transformMatrix, o.point);
        o.pos.X = point[0]/(1-(point[2]/f));
        o.pos.Y = point[1]/(1-(point[2]/f));
        var camdist = Math.sqrt(Math.pow(point[0],2)+Math.pow(point[1],2)+Math.pow(f-(point[2]),2));
        var opc = (Math.pow(f,3)-(Math.floor(camdist*100)))/100000-269;

        o.text.Opacity = opc;
        o.size.ScaleX  = opc;
        o.size.ScaleY  = opc;

        i++;
    }
}

function setTransformMatrix(x, y, z, M)
{
    vectorLength = Math.sqrt(x*x+y*y+z*z);
    if (vectorLength>.0001)
    {
        x /= vectorLength;
        y /= vectorLength;
        z /= vectorLength;
        Theta = vectorLength/100;
        cosT = Math.cos(Theta*Math.PI/180);
        sinT = Math.sin(Theta*Math.PI/180);
        tanT = 1-cosT;
        T =[[], [], []];
        T[0][0] = tanT*x*x+cosT;
        T[0][1] = tanT*x*y-sinT*z;
        T[0][2] = tanT*x*z+sinT*y;
        T[1][0] = tanT*x*y+sinT*z;
        T[1][1] = tanT*y*y+cosT;
        T[1][2] = tanT*y*z-sinT*x;
        T[2][0] = tanT*x*z-sinT*y;
        T[2][1] = tanT*y*z+sinT*x;
        T[2][2] = tanT*z*z+cosT;
        transformMatrix = mmm(T, M);
    }
}


// -------------------------------
// Multiply Matrix a by Matrix b
// -------------------------------
function mmm (a, b)
{
    var m = new Array(new Array(), new Array(), new Array());
    for (var j=0; j<3; j++) for (var i=0; i<3; i++) m[j][i] = a[j][0]*b[0][i]+a[j][1]*b[1][i]+a[j][2]*b[2][i];
    return m;
}

// -------------------------------
// Multiply Matrix a by Vector v
// -------------------------------
function mvm (a, v) {
    var m = new Array();
    for (var i=0; i<3; i++) m[i] = a[i][0]*v[0]+a[i][1]*v[1]+a[i][2]*v[2];
    return m;
}
