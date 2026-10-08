# Windows code signing

ExitEcho builds and runs without a signing certificate. Current portable builds are unsigned; do not describe them as signed releases.

For a future public release, obtain a publicly trusted code-signing identity and keep the private key outside Git and CI artifacts. Practical options are:

- [Azure Artifact Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), a managed signing service. Its [Basic tier](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku) is listed at US$9.99/month; eligibility and supported regions must be checked before purchase.
- A publicly trusted organization-validation certificate. Microsoft's [code-signing options guide](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) gives a typical US$150–300/year range and describes hardware-protected key requirements. Verify current vendor pricing and eligibility.
- [SignPath Foundation](https://signpath.org/) for qualifying open-source projects, subject to its application and approval process.

A self-signed certificate is useful for local testing only and must not be used to imply public trust.

If using a certificate in the Windows certificate store, a release operator can sign the published EXE with a securely installed certificate and a trusted timestamp service, for example:

```powershell
signtool sign /sha1 <certificate-thumbprint> /fd SHA256 /tr <trusted-rfc3161-timestamp-url> /td SHA256 ExitEcho.exe
signtool verify /pa /v ExitEcho.exe
Get-AuthenticodeSignature .\ExitEcho.exe
```

Keep certificate material and secrets out of the repository. Verify the signature and timestamp on the *final* EXE before packaging it, then compute the ZIP checksum after packaging. Without a real certificate, omit this optional signing step.
