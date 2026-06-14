namespace BIS.Sign.Models
{
    /// <summary>
    /// BISign signature format version.
    /// </summary>
    public enum BiSignVersion
    {
        /// <summary>RSA1 format — standard for Arma 3 PBO signing.</summary>
        V3 = 3,

        /// <summary>RSA2 format — used for private keys.</summary>
        V4 = 4,
    }
}
