using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Spectra;
/// <summary>Normal interactive browser. External pages receive no launcher message bridge or file mappings.</summary>
public static class CatalogBrowser
{
 public static async Task<ContentCatalog.CatalogPage?> Read(Window owner,Store store,Uri initial)
 {
  var result=new TaskCompletionSource<ContentCatalog.CatalogPage?>(TaskCreationOptions.RunContinuationsAsynchronously);
  var window=new Window{Owner=owner,Title="Каталог Minecraft — Spectra",Width=1000,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner};
  var panel=new DockPanel();var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8)};
  var back=new Button{Content="←",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
  var capture=new Button{Content="Показать список в Spectra",Padding=new Thickness(12,6,12,6),IsEnabled=false};
  var status=new TextBlock{Text="Дождитесь загрузки; при необходимости пройдите проверку сайта вручную.",Margin=new Thickness(12,8,0,0),TextWrapping=TextWrapping.Wrap};
  toolbar.Children.Add(back);toolbar.Children.Add(capture);toolbar.Children.Add(status);DockPanel.SetDock(toolbar,Dock.Top);panel.Children.Add(toolbar);
  var browser=new WebView2();panel.Children.Add(browser);window.Content=panel;
  window.Closed+=(_,_)=>{result.TrySetResult(null);browser.Dispose();};
  back.Click+=(_,_)=>{if(browser.CoreWebView2?.CanGoBack==true)browser.CoreWebView2.GoBack();};
  static string CanonicalHost(string host)=>host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?host[4..]:host;
  bool Allowed(Uri uri)=>uri.Scheme=="https"&&uri.IsDefaultPort&&string.IsNullOrEmpty(uri.UserInfo)&&(uri.Host==initial.Host||CanonicalHost(uri.Host)==CanonicalHost(initial.Host));
  window.Show();
  try{
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.Root,"catalog-webview"));
   await browser.EnsureCoreWebView2Async(environment);var core=browser.CoreWebView2;
   core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDevToolsEnabled=false;
   core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
   core.NavigationStarting+=(_,e)=>{if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)||!Allowed(uri)){e.Cancel=true;status.Text="Переход на другой сайт отменён. Для внешней загрузки используйте обычный браузер.";}};
   core.NewWindowRequested+=(_,e)=>{e.Handled=true;status.Text="Скачивание в отдельном окне: откройте страницу в обычном браузере и импортируйте файл.";};
   core.DownloadStarting+=(_,e)=>{e.Cancel=true;status.Text="Скачайте файл в обычном браузере и используйте «Импорт скачанного файла».";};
   core.NavigationCompleted+=(_,e)=>{capture.IsEnabled=e.IsSuccess;status.Text=e.IsSuccess?"Откройте список проектов и нажмите «Показать список в Spectra». Если видите проверку сайта, сначала завершите её.":"Страница не загрузилась. Попробуйте открыть сайт в обычном браузере.";};
   capture.Click+=async(_,_)=>{
    capture.IsEnabled=false;
    try{
     if(!Uri.TryCreate(core.Source,UriKind.Absolute,out var current)||!Allowed(current))throw new IOException("Неизвестная страница каталога");
     var raw=await core.ExecuteScriptAsync("document.documentElement.outerHTML");var html=JsonSerializer.Deserialize<string>(raw)??"";
     if(html.Length>4*1024*1024)throw new IOException("Страница слишком большая");
     result.TrySetResult(new ContentCatalog.CatalogPage(current,html));window.Close();
    }catch(Exception e){status.Text=e.Message;capture.IsEnabled=true;}
   };
   core.Navigate(initial.AbsoluteUri);
  }catch(Exception e){result.TrySetException(e);if(window.IsVisible)window.Close();}
  return await result.Task;
 }
}
