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

        return new CertificateHandler(X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath));
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
