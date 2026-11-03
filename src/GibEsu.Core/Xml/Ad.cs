namespace GibEsu.Core.Xml;

/// <summary>Kullanılan XML ad alanları ve algoritma adresleri.</summary>
public static class Ad
{
    public const string Ds = "http://www.w3.org/2000/09/xmldsig#";
    public const string Xades = "http://uri.etsi.org/01903/v1.3.2#";
    public const string Xades141 = "http://uri.etsi.org/01903/v1.4.1#";
    public const string Earsiv = "http://earsiv.efatura.gov.tr";
    public const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    public const string Soap12 = "http://www.w3.org/2003/05/soap-envelope";
    public const string Web = "http://earsiv.vedop3.ggm.gov.org/";
    public const string Wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    public const string Wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    public const string ExcC14n = "http://www.w3.org/2001/10/xml-exc-c14n#";
    public const string IncC14n = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    public const string Enveloped = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";

    public const string Sha1 = "http://www.w3.org/2000/09/xmldsig#sha1";
    public const string Sha256 = "http://www.w3.org/2001/04/xmlenc#sha256";
    public const string Sha384 = "http://www.w3.org/2001/04/xmldsig-more#sha384";
    public const string Sha512 = "http://www.w3.org/2001/04/xmlenc#sha512";

    public const string EcdsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    public const string EcdsaSha384 = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384";
    public const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

    public const string X509v3 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
    public const string Base64Binary = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    public const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";
}
