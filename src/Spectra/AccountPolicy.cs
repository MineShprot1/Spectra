using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Spectra;
public static class AccountPolicy
{
 public static string OfflineUuid(string name){var bytes=MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:"+name));bytes[6]=(byte)((bytes[6]&15)|48);bytes[8]=(byte)((bytes[8]&63)|128);return Convert.ToHexString(bytes).ToLowerInvariant();}
 public static void ValidateName(string name){if(!Regex.IsMatch(name,@"\A[A-Za-z0-9_]{3,16}\z"))throw new IOException("Ник: 3–16 латинских букв, цифр или подчёркиваний");}
}
