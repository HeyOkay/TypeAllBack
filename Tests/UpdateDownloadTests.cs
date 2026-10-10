using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using LayoutPreviewPrototype;

internal static class UpdateDownloadTests
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"TypeAllBack-update-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] data=System.Text.Encoding.UTF8.GetBytes("checked-installer-fixture");
        var package=new UpdatePackage(new Version(2,0,0),new Uri("https://github.com/HeyOkay/TypeAllBack/releases/download/v2.0.0/TypeAllBack-2.0.0-win-x64.msi"),Convert.ToHexString(SHA256.HashData(data)),data.Length);
        var progress=new Progress<DownloadProgress>(_=>{});
        try
        {
            using(var service=new UpdateChecker(new DownloadHandler(data)))
            {
                string path=service.DownloadAsync(package,progress,CancellationToken.None,root).GetAwaiter().GetResult();
                check(File.ReadAllBytes(path).SequenceEqual(data),"verified download preserves bytes");
                check(!File.Exists(path+".part"),"only verified installer gets final file name");
                File.Delete(path);
            }
            using(var service=new UpdateChecker(new DownloadHandler(data)))
            {
                bool failed=false;
                try{service.DownloadAsync(package with{Sha256=new string('0',64)},progress,CancellationToken.None,root).GetAwaiter().GetResult();}
                catch(InvalidDataException){failed=true;}
                check(failed,"wrong installer digest fails closed");
                check(!Directory.EnumerateFiles(root,"*.part",SearchOption.AllDirectories).Any(),"failed digest removes partial download");
            }
            using(var service=new UpdateChecker(new DownloadHandler(data,new Uri("https://example.com/foreign.msi"))))
            {
                bool failed=false;
                try{service.DownloadAsync(package,progress,CancellationToken.None,root).GetAwaiter().GetResult();}
                catch(InvalidDataException){failed=true;}
                check(failed,"download cannot redirect to unrelated host");
            }
            using(var service=new UpdateChecker(new DownloadHandler(data)))
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();bool stopped=false;
                try{service.DownloadAsync(package,progress,cancellation.Token,root).GetAwaiter().GetResult();}
                catch(OperationCanceledException){stopped=true;}
                check(stopped,"cancelled download never returns installer");
            }
        }
        finally
        {
            // This fixture creates only fresh empty child folders, not user data.
            foreach(string child in Directory.GetDirectories(root)) Directory.Delete(child);
            Directory.Delete(root);
        }
    }
    private sealed class DownloadHandler(byte[] content,Uri? redirect=null):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var response=new HttpResponseMessage(redirect==null?HttpStatusCode.OK:HttpStatusCode.Redirect);
            if(redirect==null)response.Content=new ByteArrayContent(content);else response.Headers.Location=redirect;
            return Task.FromResult(response);
        }
    }
}
