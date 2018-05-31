using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Drawing;
using Microsoft.Win32;
using System.Threading;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

static class Wallhaven
{
	const string BaseUrl = "https://alpha.wallhaven.cc/search?categories=100&purity=100&resolutions=1920x1080&order=desc";	

	static string GetHTML(string url)
	{
		try
		{
			using (var web = new WebClient())
			{
				return web.DownloadString(url);
			}			
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Download of {url} failed: {ex.Message}");
			return string.Empty;
		}
	}

	static string[] ImageUrls(string url)
	{
		var imageUrls = new List<string>();

		var html = GetHTML(url);
		if (string.IsNullOrEmpty(html)) return imageUrls.ToArray();

		var matches = Regex.Matches(html, @"https\:\/\/alpha.wallhaven.cc\/wallpaper/\d+");

		foreach (Match match in matches)
		{
		    foreach (Capture capture in match.Captures)
		    {
		    	var imageUrl = capture.Value.Replace(
		    		@"https://alpha.wallhaven.cc/wallpaper/", 
		    		$"https://wallpapers.wallhaven.cc/wallpapers/full/wallhaven-"
	    		) + ".jpg";

				if (!imageUrls.Contains(imageUrl)) imageUrls.Add(imageUrl);
		    }
		}

		return imageUrls.ToArray();
	}

	public static Image Download(string url)
	{
		try
		{
			using (var web = new WebClient())
			{
				var bytes = web.DownloadData(url);
				var stream = new MemoryStream(bytes);
				return Image.FromStream(stream);
			}			
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Download of {url} failed: {ex}");
			return null;
		}
	}

	static IQueryable<Image> Images(params string[] urls) => urls.Select(Download).Where(img => img != null).AsQueryable();

	public static IQueryable<Image> Random() => Images(ImageUrls($"{BaseUrl}&sorting=random"));

	public static IQueryable<Image> Search(string term) => Images(ImageUrls($"{BaseUrl}&q={term}&sorting=date_added"));
}

class FullScreenForm : Form
{
	static readonly Screen DefaultScreen = Screen.PrimaryScreen; // AllScreens[1];
	public Screen[] Screens { get; } = Screen.AllScreens;
	public Screen Screen { get; set; } = DefaultScreen;

	public string GetResolution() 
	{
		return $"{Screen.Bounds.Width}x{Screen.Bounds.Height}";
	}

	public FullScreenForm()
	{
		SuspendLayout();
		Size = new Size(0, 0);
		DoubleBuffered = true;
		BackColor = Color.Black;
		FormBorderStyle = FormBorderStyle.None;
		WindowState = FormWindowState.Maximized;
		StartPosition = FormStartPosition.Manual;
		// Bounds?
		Location = Screen.WorkingArea.Location;
		KeyPreview = true;
		KeyUp += KeyPressed;
		ShowInTaskbar = false;
		ResumeLayout();
	}

	public void MoveToScreen(Screen screen)
	{
		Screen = screen;
		FormBorderStyle = FormBorderStyle.Fixed3D;
		WindowState = (FormWindowState)0;
		Location = screen.WorkingArea.Location; // Bounds?
		WindowState = FormWindowState.Maximized;
		FormBorderStyle = FormBorderStyle.None;
	}

	Screen GetScreenFromKey(Keys key)
	{
		var index = int.Parse(key.ToString().Trim('F').Trim());
		return index < Screens.Length ? Screens[index] : Screens[index % Screens.Length];
	}

	void KeyPressed(object sender, KeyEventArgs e)
	{
		if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F4)
		{
			MoveToScreen(GetScreenFromKey(e.KeyCode));
		}		
	}
}

public sealed class Wallpaper
{
    Wallpaper() { }

    const int SPI_SETDESKWALLPAPER = 20;
    const int SPIF_UPDATEINIFILE = 0x01;
    const int SPIF_SENDWININICHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    public enum Style
    {
        Tiled,
        Centered,
        Stretched
    }

    public static void Set(Image img, Style style)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "wallpaper.bmp");
        img.Save(tempPath, System.Drawing.Imaging.ImageFormat.Bmp);
        RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);

        if (style == Style.Stretched)
        {
            key.SetValue(@"WallpaperStyle", 2.ToString());
            key.SetValue(@"TileWallpaper", 0.ToString());
        }
        else if (style == Style.Centered)
        {
            key.SetValue(@"WallpaperStyle", 1.ToString());
            key.SetValue(@"TileWallpaper", 0.ToString());
        }
        else if (style == Style.Tiled)
        {
            key.SetValue(@"WallpaperStyle", 1.ToString());
            key.SetValue(@"TileWallpaper", 1.ToString());
        }

        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, tempPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
    }
}

class Desktop : FullScreenForm
{
	Image wallpaper = null;

	public Image Background
	{ 
		get { return wallpaper; }
		set { SetWallpaper(value); }
	}

	public Desktop()
	{
		Opacity = 0.1;
		Load += (s, e) => SendToBack();
		Shown += (s, e) => SendToBack();
		Activated += (s, e) => SendToBack();
		Invalidated += (s, e) => SendToBack();
	}

	public void SetWallpaper(Image image, Wallpaper.Style style=Wallpaper.Style.Stretched)
	{
		try
		{
			BackgroundImage = image;
			BackgroundImageLayout = ImageLayout.Stretch;
			Wallpaper.Set(image, style);
		}
		catch(Exception ex)
		{
			Console.WriteLine($"Failed to set wallpaper: {ex}");
		}
	}
}

class Overlay : Desktop
{
	public Overlay(FullScreenForm owner)
	{
		Owner = owner;
		Opacity = 0.75;
		Owner.LocationChanged += SyncToOwner;
		DoubleClick += (s, e) => Hide();
		KeyPreview = true;
		KeyUp += KeyPressed;
	}

	void KeyPressed(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Escape) Hide();
	}

	void SyncToOwner(object sender, EventArgs e)
	{
		MoveToScreen((Owner as FullScreenForm).Screen);
	}
}

class SearchOverlay : Overlay
{
	TextBox SearchBox { get; }
	Panel Grid { get; }
	Label SearchLabel { get; }
	Label CloseButton { get; }

	Font SearchFont = new Font("Calibri Light", 18);
	BackgroundWorker Searcher = new BackgroundWorker();
	const string SearchLabelText = "Enter URL, File Path or a Search";

	public SearchOverlay(FullScreenForm owner) : base(owner)
	{
		// Bounds?
		int ScreenWidth = Screen.WorkingArea.Width - Width;
		int ScreenHeight = Screen.WorkingArea.Height - Height;

		var SearchPanel = new Panel
    	{
    		Height = 71,
    		BackColor = Color.Gray,
    		Width = (ScreenWidth / 5) * 2,
    		Location = new Point(ScreenWidth / 3, ScreenHeight / 3),
    	};

    	SearchLabel = new Label
    	{
    		Height = 40,
    		Text = SearchLabelText,
    		Font = SearchFont,
    		Dock = DockStyle.Top,
    		BackColor = Color.Black,
    		ForeColor = Color.WhiteSmoke,
    	};

    	SearchBox = new TextBox
		{
			Height = 40,
			Font = SearchFont,
			Dock = DockStyle.Top,
			BackColor = Color.Black,
			Margin = new Padding(10),
			ForeColor = Color.WhiteSmoke,
			BorderStyle = BorderStyle.None,
		};

		SearchPanel.Controls.AddRange(new Control[] 
		{ 
			SearchBox, 
			SearchLabel 
		});

		Grid = new Panel()
		{
			AutoScroll = false,
			Dock = DockStyle.Bottom,
    			Height = 100,
			Padding = new Padding(0, 0, 0, 40),
			BackColor = Color.Black,
		};

		Grid.VerticalScroll.Enabled = true;
		Grid.VerticalScroll.Visible = false;
		Grid.HorizontalScroll.Enabled = true;
		Grid.HorizontalScroll.Visible = false;

		CloseButton = new Label
		{
			Text = "×",
			Width = 50,
			Height = 50,
			Font = new Font(SearchFont.Name, 20), // 30
			ForeColor = Color.White,
			BackColor = Color.Black,
			TabStop = false,
			Location = new Point(Screen.WorkingArea.Width - 30, 0) // 50
		};

		var Logo = new Label
		{
			AutoSize = true,
			Font = SearchFont,
			ForeColor = Color.White,
			Dock = DockStyle.Left,
			Text = $"Desktop2 ({GetResolution()})"
		};

		CloseButton.Click += (s, e) => (Owner as Form).Close();
		
		Controls.AddRange(new Control[] 
		{
			Logo,
			CloseButton,
			Grid,
			SearchPanel
		});

		Searcher.DoWork += (s, e) => 
		{
			Status("Searching...");
			var term = (string)e.Argument;
			e.Result = term != null ? Wallhaven.Search(term) : Wallhaven.Random();
		};

		Searcher.RunWorkerCompleted += (s, e) => 
		{
			LoadImages((IEnumerable<Image>)e.Result);
			Status();
		};

		SearchBox.KeyUp += Search;
		Searcher.RunWorkerAsync();
		SearchBox.Focus();
	}

	void Status(string status=null)
	{
		status = status ?? SearchLabelText;
		SearchLabel.Text = status;
		SearchLabel.Update();
	}

	PictureBox Thumbnail(Image image)
	{
		if (image == null) return null;
		
		var picture = new PictureBox 
		{
			Image = image,
			Dock = DockStyle.Left,
	 		SizeMode = PictureBoxSizeMode.StretchImage,
		};

		picture.MouseEnter += (s, e) => 
		{	
			Owner.Opacity = 1.0;
		};

		picture.MouseLeave += (s, e) => 
		{
			Owner.Opacity = 0.1;
		};

		picture.Click += (s, e) => Preview(picture.Image);
		picture.DoubleClick += (s, e) => SetWallpaper(picture.Image);
		return picture;
	}

	void SetWallpaper(Image image)
	{
		(Owner as Desktop).SetWallpaper(image); 
		Hide(); 
	}

	void Preview(Image image)
	{
		(Owner as Desktop).BackgroundImage = image; 
		SearchBox.Focus();
	}

	void LoadImages(IEnumerable<Image> images)
	{
		Grid.Controls.Clear();

		foreach (var image in images)
		{
			var picture = Thumbnail(image);
			if (picture != null) Grid.Controls.Add(picture);
		}
	}

	bool IsUrl(string text)
	{
		text = text.ToLower().Trim();
		var starts = new string[] { "http:", "https:", "www.", "ftp:" };
		return starts.Any(start => text.StartsWith(start));
	}

	bool IsYoutubeVideo(string text)
	{
		text = text.ToLower().Trim();
		var tubes = new string[] { "youtube", "youtu.be" };
		return tubes.Any(tube => text.Contains(tube));
	}

	string GetFullScreenYoutubeUrl(string url)
	{
		var videoID = url.Split('/').Last();
		return $"https://www.youtube.com/v/{videoID}&autoplay=1&controls=0&loop=1";
	}

	void Browse(string url)
	{
		var browser = new WebBrowser { Dock = DockStyle.Fill };
		(Owner as Form).Controls.Add(browser);
		(Owner as Form).Opacity = 1.0;
		(Owner as Form).Controls.Add(CloseButton);
		browser.Navigate(url);
	}

	void Search(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Enter)
		{
			var term = SearchBox.Text.Trim();
			if (term.Length == 0) return;

			if (IsUrl(term))
			{
				Status("Downloading...");

				// https://youtu.be/0fYL_qiDYf0
				if (IsYoutubeVideo(term))
				{
					var url = GetFullScreenYoutubeUrl(term);
					Browse(url);
				}
				else
				{
					var image = Wallhaven.Download(term);

					if (image != null)
					{
						SetWallpaper(image);					
					}
					else
					{
						Browse(term);
					}
				}

				Status();
			}
			else if (term.Contains("\\"))
			{
				Status("Opening...");
				SetWallpaper(Image.FromFile(term));
				Status();
			}
			else if (!Searcher.IsBusy) 
			{
				Searcher.RunWorkerAsync(term);
			}
		}
	}
}

class Desktop2 : Desktop
{
	public Overlay Search { get; }

	public Desktop2(bool random=true)
	{
		Search = new SearchOverlay(this);
		DoubleClick += (s, e) => Search.Show();
		if (random) Shown += RandomWallpaper;
	}

	public void RandomWallpaper(object sender, EventArgs e)
	{
		SetWallpaper(Wallhaven.Random().FirstOrDefault());	
	}
}

class Program
{
	[STAThread]
	static void Main(string[] args)
	{
		var random = args.Length == 1  && args[0] == "random";
		Application.EnableVisualStyles();
		Application.Run(new Desktop2(random));
	}
}
