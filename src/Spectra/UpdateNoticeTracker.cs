namespace Spectra;
// Deduplicate actual announcements, not silent startup observations. In particular,
// the first successful poll after a failed startup check is still an announcement.
public sealed class UpdateNoticeTracker
{
 readonly HashSet<string> seen=new(StringComparer.Ordinal);
 public bool Observe(string commit,bool available)
 {
  if(commit.Length!=40||!commit.All(Uri.IsHexDigit))return false;
  return available&&seen.Add(commit);
 }
}
