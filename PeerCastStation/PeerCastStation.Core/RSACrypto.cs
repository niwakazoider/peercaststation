using System.Security.Cryptography;
using System;

namespace PeerCastStation.Core
{
  public class RSACrypto
  {
    private RSACryptoServiceProvider csp = new RSACryptoServiceProvider(1024);
    private byte[] privateKey = new byte[0];
    public string publicKey {
      get {
        try {
          return Convert.ToBase64String(csp.ExportCspBlob(false));
        }catch(Exception) { }
        return "";
      }
      set {
        try {
          var keyBlob = Convert.FromBase64String(value);
          csp.ImportCspBlob(keyBlob);
        }catch(Exception) { }
      }
    }
    public RSACrypto()
    {
      privateKey = csp.ExportCspBlob(true);
    }
    public bool HasBroadcastPublicKey()
    {
      return csp.PublicOnly;
    }
    public byte[] Sign(byte[] data)
    {
      var hash = SHA256.Create().ComputeHash(data);
      return csp.SignHash(hash, CryptoConfig.MapNameToOID("SHA256"));
    }
    public bool Verify(byte[] data, byte[] sign)
    {
      var hash = SHA256.Create().ComputeHash(data);
      return csp.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA256"), sign);
    }
  }
}
