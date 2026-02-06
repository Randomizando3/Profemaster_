using System.Text;

namespace ProfeMaster.Pages.Help.Tutoriais;

public partial class TutorialModalPage : ContentPage
{
    private readonly string _fileName;

    public TutorialModalPage(string fileName, string? title = null)
    {
        InitializeComponent();

        _fileName = fileName;

        TitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Tutorial" : title;
        SubLabel.Text = fileName;

        Loaded += async (_, _) => await LoadHtmlAsync();
    }

    private async Task LoadHtmlAsync()
    {
        try
        {
            var assetPath = $"Pages/Help/Tutoriais/{_fileName}";

            await using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = await reader.ReadToEndAsync();

#if ANDROID
            // Em Android, assets ficam em android_asset
            var baseUrl = "file:///android_asset/";
#elif WINDOWS
        // Em Windows, WebView2 resolve relativo a um diretório local
        // Como estamos carregando via string, apontamos para o diretório do app package
        var baseUrl = "ms-appx-web:///"; 
#elif IOS || MACCATALYST
        // iOS/mac: funciona apontando para o bundle
        var baseUrl = "file:///";
#else
        var baseUrl = "";
#endif

            Web.Source = new HtmlWebViewSource
            {
                Html = html,
                BaseUrl = baseUrl
            };
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível abrir o tutorial:\n{_fileName}\n\n{ex.Message}", "OK");
        }
    }


    private async void OnBack(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
