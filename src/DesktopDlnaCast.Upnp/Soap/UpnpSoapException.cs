using System.Net;

namespace DesktopDlnaCast.Upnp.Soap;

public sealed class UpnpSoapException : Exception
{
    public UpnpSoapException()
    {
    }

    public UpnpSoapException(string message)
        : base(message)
    {
    }

    public UpnpSoapException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UpnpSoapException(HttpStatusCode statusCode, SoapFault? fault)
        : base(CreateMessage(statusCode, fault))
    {
        StatusCode = statusCode;
        Fault = fault;
    }

    public HttpStatusCode? StatusCode { get; }

    public SoapFault? Fault { get; }

    private static string CreateMessage(HttpStatusCode statusCode, SoapFault? fault)
    {
        string detail = fault?.UpnpErrorDescription ?? fault?.FaultString ?? "No SOAP Fault details were supplied.";
        return $"The UPnP SOAP request failed with HTTP {(int)statusCode}: {detail}";
    }
}

