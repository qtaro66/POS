using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCertificateHttpHandlerFactory(IConfiguration configuration)
    : IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
{
    public HttpMessageHandler CreateHandler()
    {
        var certificatePath = configuration["BranchCentralMtls:CertificatePath"];
        var privateKeyPath = configuration["BranchCentralMtls:PrivateKeyPath"];
        if (string.IsNullOrWhiteSpace(certificatePath) || !Path.IsPathFullyQualified(certificatePath)
            || string.IsNullOrWhiteSpace(privateKeyPath) || !Path.IsPathFullyQualified(privateKeyPath))
        {
            throw new InvalidOperationException("Branch mTLS requires absolute certificate and private key paths.");
        }

        var certificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
        if (!OperatingSystem.IsWindows()) { return new CertificateHandler(certificate); }

        // Schannel requires a key backed by the Windows key store.
        using (certificate)
        {
            var pkcs12 = certificate.Export(X509ContentType.Pkcs12);
            try
            {
                return new CertificateHandler(X509CertificateLoader.LoadPkcs12(pkcs12, null));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
        }
    }

    private sealed class CertificateHandler : HttpClientHandler
    {
        private readonly X509Certificate2 _certificate;

        public CertificateHandler(X509Certificate2 certificate)
        {
            _certificate = certificate;
            AllowAutoRedirect = false;
            ClientCertificates.Add(certificate);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { _certificate.Dispose(); }
        }
    }
}
