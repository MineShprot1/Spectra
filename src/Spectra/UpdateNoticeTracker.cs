namespace Spectra;
// Session-only baseline: a restart never re-announces a commit already on the server.
public sealed class UpdateNoticeTracker
{
 string latest="";
 readonly HashSet<string> seen=new(StringComparer.Ordinal);
 public bool Observe(string commit,bool available)
 {
  if(commit.Length!=40||!commit.All(Uri.IsHexDigit))return false;
  bool initial=latest.Length==0;latest=commit;
  return seen.Add(commit)&&!initial&&available;
 }
}
