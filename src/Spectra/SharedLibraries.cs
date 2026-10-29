using System.IO;
namespace Spectra;
public sealed class SharedLibraries(Store store)
{
 Task? migration;
 public Task Prepare()=>migration??=Task.Run(()=>{
  var target=Path.Combine(store.Root,"cache","libraries");var marker=Path.Combine(target,".spectra-migrated");if(File.Exists(marker))return;Directory.CreateDirectory(target);
  var instances=Path.Combine(store.Root,"instances");if(Directory.Exists(instances)){
   var options=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=true};
   foreach(var instance in Directory.EnumerateDirectories(instances)){
    var source=Path.Combine(instance,".minecraft","libraries");if(!Directory.Exists(source)||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)continue;
    foreach(var file in Directory.EnumerateFiles(source,"*.jar",options)){
     var destination=Store.SafePath(target,Path.GetRelativePath(source,file));if(File.Exists(destination)||new FileInfo(file).Length==0)continue;
     Directory.CreateDirectory(Path.GetDirectoryName(destination)!);var temporary=destination+".migrate-"+Guid.NewGuid().ToString("N")+".part";
     try{File.Copy(file,temporary);File.Move(temporary,destination,false);}catch(IOException){/* The installer validates or downloads any missing library. */}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
   }
  }
  File.WriteAllText(marker,"1");
 });
}
