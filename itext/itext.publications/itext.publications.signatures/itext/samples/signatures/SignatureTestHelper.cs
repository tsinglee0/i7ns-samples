/*

This file is part of the iText (R) project.
Copyright (c) 1998-2019 iText Group NV

*/

using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.X509;
using iText.Forms;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Utils;
using iText.Signatures;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security.Certificates;

namespace iText.Samples.Signatures
{
    public class SignatureTestHelper
    {
        public static readonly string ADOBE = "../../resources/encryption/adobeRootCA.cer";

        public static readonly string CACERT = "../../resources/encryption/CACertSigningAuthority.crt";

        public static readonly string BRUNO = "../../resources/encryption/bruno.crt";

        private String errorMessage;

        protected internal virtual String CheckForErrors(String outFile, String cmpFile, String destPath, 
            IDictionary<int, IList<Rectangle>> ignoredAreas)
        {
            errorMessage = null;
            
            //compares documents visually
            CompareTool ct = new CompareTool();
            String comparisonResult = ct.CompareVisually(outFile, cmpFile, destPath, "diff", ignoredAreas);
            AddError(comparisonResult);
            
            //verifies document signatures
            VerifySignaturesForDocument(outFile);
            
            //compares document signatures with signatures in cmp_file
            CompareSignatures(outFile, cmpFile);
            if (errorMessage != null)
            {
                errorMessage = "\n" + outFile + ":\n" + errorMessage + "\n";
            }

            return errorMessage;
        }

        /// <summary>In this method we add trusted certificates.</summary>
        /// <remarks>
        /// In this method we add trusted certificates.
        /// If document signatures certificates doesn't contain certificates that are added in this method, verification will fail.
        /// NOTE: Override this method to add additional certificates.
        /// </remarks>
        protected internal virtual void InitKeyStoreForVerification(List<X509Certificate> ks)
        {
            var parser = new X509CertificateParser();
            X509Certificate adobeCert;
            X509Certificate cacertCert;
            X509Certificate brunoCert;
            using (FileStream adobeStream = new FileStream(ADOBE, FileMode.Open, FileAccess.Read),
                cacertStream = new FileStream(CACERT, FileMode.Open, FileAccess.Read),
                brunoStream = new FileStream(BRUNO, FileMode.Open, FileAccess.Read))
            {
                adobeCert = parser.ReadCertificate(adobeStream);
                cacertCert = parser.ReadCertificate(cacertStream);
                brunoCert = parser.ReadCertificate(brunoStream);
            }

            ks.Add(adobeCert);
            ks.Add(cacertCert);
            ks.Add(brunoCert);
        }

        protected static X509Certificate LoadCertificateFromKeyStore(String keystorePath, char[] ksPass)
        {
            string alias = null;
            Pkcs12Store pk12 = new Pkcs12Store(new FileStream(keystorePath, FileMode.Open, FileAccess.Read), ksPass);

            foreach (var a in pk12.Aliases)
            {
                alias = ((string) a);
                if (pk12.IsKeyEntry(alias))
                    break;
            }

            return pk12.GetCertificate(alias).Certificate;
        }

        private void VerifySignaturesForDocument(String documentPath)
        {
            PdfReader reader = new PdfReader(documentPath);
            PdfDocument pdfDoc = new PdfDocument(new PdfReader(documentPath));
            SignatureUtil signUtil = new SignatureUtil(pdfDoc);
            IList<String> names = signUtil.GetSignatureNames();
            VerifySignatures(signUtil, names);
            reader.Close();
        }

        private void VerifySignatures(SignatureUtil signUtil, IList<String> names)
        {
            foreach (String name in names)
            {
                PdfPKCS7 pkcs7 = signUtil.ReadSignatureData(name);
                
                // verify signature integrity
                if (!pkcs7.VerifySignatureIntegrityAndAuthenticity())
                {
                    AddError(String.Format("\"{0}\" signature integrity is invalid\n", name));
                }

                VerifyCertificates(pkcs7);
            }
        }

        private void VerifyCertificates(PdfPKCS7 pkcs7)
        {
            List<X509Certificate> ks = new List<X509Certificate>();
            InitKeyStoreForVerification(ks);
            X509Certificate[] certs = pkcs7.GetSignCertificateChain();
            DateTime cal = pkcs7.GetSignDate();
            IList<VerificationException> errors = CertificateVerification.VerifyCertificates(
                certs, ks, cal);
            if (errors.Count > 0)
            {
                foreach (VerificationException e in errors)
                {
                    AddError(e.Message + "\n");
                }
            }

            for (int i = 0; i < certs.Length; i++)
            {
                X509Certificate cert = (X509Certificate) certs[i];
                CheckCertificateInfo(cert, cal.ToUniversalTime(), pkcs7);
            }

            X509Certificate signCert = (X509Certificate) certs[0];
            X509Certificate issuerCert = (certs.Length > 1 ? (X509Certificate) certs[1] : null);
            
            //Checking validity of the document at the time of signing
            CheckRevocation(pkcs7, signCert, issuerCert, cal.ToUniversalTime());
        }

        private void CheckCertificateInfo(X509Certificate cert, DateTime signDate, PdfPKCS7
            pkcs7)
        {
            try
            {
                cert.CheckValidity(signDate);
            }
            catch (CertificateExpiredException)
            {
                AddError("The certificate was expired at the time of signing.");
            }
            catch (CertificateNotYetValidException)
            {
                AddError("The certificate wasn't valid yet at the time of signing.");
            }

            if (pkcs7.GetTimeStampDate() != DateTime.MaxValue)
            {
                if (!pkcs7.VerifyTimestampImprint())
                {
                    AddError("Timestamp is invalid.");
                }
            }
        }

        private void CheckRevocation(PdfPKCS7 pkcs7, X509Certificate signCert, X509Certificate issuerCert, 
            DateTime date)
        {
            IList<BasicOcspResp> ocsps = new List<BasicOcspResp>();
            if (pkcs7.GetOcsp() != null)
            {
                ocsps.Add(pkcs7.GetOcsp());
            }

            OCSPVerifier ocspVerifier = new OCSPVerifier(null, ocsps);
            IList<VerificationOK> verification = ocspVerifier.Verify(signCert, issuerCert, date);
            if (verification.Count == 0)
            {
                IList<X509Crl> crls = new List<X509Crl>();
                if (pkcs7.GetCRLs() != null)
                {
                    foreach (X509Crl crl in pkcs7.GetCRLs())
                    {
                        crls.Add((X509Crl) crl);
                    }
                }

                CRLVerifier crlVerifier = new CRLVerifier(null, crls);
                var verOks = crlVerifier.Verify(signCert, issuerCert, date);
                foreach (VerificationOK verOk in verOks)
                {
                    verification.Add(verOk);
                }
            }
        }

        //if exception was not thrown document is not revoked or it couldn't be verified
        protected internal virtual void CompareSignatures(String outFile, String cmpFile)
        {
            SignedDocumentInfo outInfo = CollectInfo(outFile);
            SignedDocumentInfo cmpInfo = CollectInfo(cmpFile);
            CompareSignedDocumentsInfo(outInfo, cmpInfo);
        }

        private SignedDocumentInfo CollectInfo(String documentPath)
        {
            SignedDocumentInfo docInfo = new SignedDocumentInfo();
            PdfDocument pdfDoc = new PdfDocument(new PdfReader(documentPath));
            PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, false);
            SignatureUtil signUtil = new SignatureUtil(pdfDoc);
            IList<String> names = signUtil.GetSignatureNames();
            docInfo.SetNumberOfTotalRevisions(signUtil.GetTotalRevisions());
            SignaturePermissions perms = null;
            IList<SignatureInfo> signInfos = new List<SignatureInfo>();
            foreach (String name in names)
            {
                SignatureInfo sigInfo = new SignatureInfo();
                sigInfo.SetSignatureName(name);
                sigInfo.SetRevisionNumber(signUtil.GetRevision(name));
                sigInfo.SetSignatureCoversWholeDocument(signUtil.SignatureCoversWholeDocument(name));
                IList<PdfWidgetAnnotation> widgetAnnotationsList = form.GetField(name).GetWidgets();
                if (widgetAnnotationsList != null && widgetAnnotationsList.Count > 0)
                {
                    sigInfo.SetSignaturePosition(widgetAnnotationsList[0].GetRectangle().ToRectangle());
                }

                PdfPKCS7 pkcs7 = signUtil.ReadSignatureData(name);
                sigInfo.SetDigestAlgorithm(pkcs7.GetHashAlgorithm());
                sigInfo.SetEncryptionAlgorithm(pkcs7.GetEncryptionAlgorithm());
                PdfName filterSubtype = pkcs7.GetFilterSubtype();
                if (filterSubtype != null)
                {
                    sigInfo.SetFilterSubtype(filterSubtype.ToString());
                }

                X509Certificate signCert = pkcs7.GetSigningCertificate();
                sigInfo.SetSignerName(iText.Signatures.CertificateInfo.GetSubjectFields(signCert).GetField("CN"));
                sigInfo.SetAlternativeSignerName(pkcs7.GetSignName());
                sigInfo.SetSignDate(pkcs7.GetSignDate().ToUniversalTime());
                if (pkcs7.GetTimeStampDate() != DateTime.MaxValue)
                {
                    sigInfo.SetTimeStamp(pkcs7.GetTimeStampDate().ToUniversalTime());
                    TimeStampToken ts = pkcs7.GetTimeStampToken();
                    sigInfo.SetTimeStampService(ts.TimeStampInfo.Tsa.ToString());
                }

                sigInfo.SetLocation(pkcs7.GetLocation());
                sigInfo.SetReason(pkcs7.GetReason());
                PdfDictionary sigDict = signUtil.GetSignatureDictionary(name);
                PdfString contactInfo = sigDict.GetAsString(PdfName.ContactInfo);
                if (contactInfo != null)
                {
                    sigInfo.SetContactInfo(contactInfo.ToString());
                }

                perms = new SignaturePermissions(sigDict, perms);
                sigInfo.SetIsCertifiaction(perms.IsCertification());
                sigInfo.SetIsFieldsFillAllowed(perms.IsFillInAllowed());
                sigInfo.SetIsAddingAnnotationsAllowed(perms.IsAnnotationsAllowed());
                IList<String> fieldLocks = new List<String>();
                foreach (SignaturePermissions.FieldLock Lock in perms.GetFieldLocks())
                {
                    fieldLocks.Add(Lock.ToString());
                }

                sigInfo.SetFieldsLocks(fieldLocks);
                X509Certificate[] certs = pkcs7.GetSignCertificateChain();
                IList<CertificateInfo> certInfos = new List<CertificateInfo>();
                for (int i = 0; i < certs.Length; i++)
                {
                    X509Certificate cert = (X509Certificate) certs[i];
                    CertificateInfo certInfo = new CertificateInfo();
                    certInfo.SetIssuer(cert.IssuerDN);
                    certInfo.SetSubject(cert.SubjectDN);
                    certInfo.SetValidFrom(cert.NotBefore);
                    certInfo.SetValidTo(cert.NotAfter);
                    certInfos.Add(certInfo);
                }

                sigInfo.SetCertificateInfos(certInfos);
                signInfos.Add(sigInfo);
            }

            docInfo.SetSignatureInfos(signInfos);
            return docInfo;
        }

        private void CompareSignedDocumentsInfo(SignedDocumentInfo outInfo, SignedDocumentInfo cmpInfo)
        {
            if (outInfo.GetNumberOfTotalRevisions() != cmpInfo.GetNumberOfTotalRevisions())
            {
                AddComparisonError("Number of total revisions", outInfo.GetNumberOfTotalRevisions().ToString(),
                    cmpInfo.GetNumberOfTotalRevisions().ToString());
            }

            if (outInfo.GetSignatureInfos().Count != cmpInfo.GetSignatureInfos().Count)
            {
                AddComparisonError("Number of signatures in document", outInfo.GetSignatureInfos().Count.ToString(), 
                    cmpInfo.GetSignatureInfos().Count.ToString());
            }

            for (int i = 0; i < outInfo.GetSignatureInfos().Count; ++i)
            {
                SignatureInfo outSig = outInfo.GetSignatureInfos()[i];
                SignatureInfo cmpSig = cmpInfo.GetSignatureInfos()[i];
                String outAltName = outSig.GetAlternativeSignerName();
                String cmpAltName = cmpSig.GetAlternativeSignerName();
                if (CheckIfEqual(outAltName, cmpAltName))
                {
                    AddComparisonError("Alternative signer name", outAltName, cmpAltName);
                }

                String outContactInfo = outSig.GetContactInfo();
                String cmpContactInfo = cmpSig.GetContactInfo();
                if (CheckIfEqual(outContactInfo, cmpContactInfo))
                {
                    AddComparisonError("Contact info", outContactInfo, cmpContactInfo);
                }

                String outDigestAlg = outSig.GetDigestAlgorithm();
                String cmpDigestAlg = cmpSig.GetDigestAlgorithm();
                if (CheckIfEqual(outDigestAlg, cmpDigestAlg))
                {
                    AddComparisonError("Digest algorithm", outDigestAlg, cmpDigestAlg);
                }

                String outEncryptAlg = outSig.GetEncryptionAlgorithm();
                String cmpEncryptAlg = cmpSig.GetEncryptionAlgorithm();
                if (CheckIfEqual(outEncryptAlg, cmpEncryptAlg))
                {
                    AddComparisonError("Encryption algorithm", outEncryptAlg, cmpEncryptAlg);
                }

                String outLocation = outSig.GetLocation();
                String cmpLocation = cmpSig.GetLocation();
                if (CheckIfEqual(outLocation, cmpLocation))
                {
                    AddComparisonError("Location", outLocation, cmpLocation);
                }

                String outReason = outSig.GetReason();
                String cmpReason = cmpSig.GetReason();
                if (CheckIfEqual(outReason, cmpReason))
                {
                    AddComparisonError("Reason", outReason, cmpReason);
                }

                String outSigName = outSig.GetSignatureName();
                String cmpSigName = cmpSig.GetSignatureName();
                if (CheckIfEqual(outSigName, cmpSigName))
                {
                    AddComparisonError("Signature name", outSigName, cmpSigName);
                }

                String outSignerName = outSig.GetSignerName();
                String cmpSignerName = cmpSig.GetSignerName();
                if (CheckIfEqual(outSignerName, cmpSignerName))
                {
                    AddComparisonError("Signer name", outSignerName, cmpSignerName);
                }

                String outFilterSubtype = outSig.GetFilterSubtype();
                String cmpFilterSubtype = cmpSig.GetFilterSubtype();
                if (CheckIfEqual(outFilterSubtype, cmpFilterSubtype))
                {
                    AddComparisonError("Filter subtype", outFilterSubtype, cmpFilterSubtype);
                }

                int outSigRevisionNumber = outSig.GetRevisionNumber();
                int cmpSigRevisionNumber = cmpSig.GetRevisionNumber();
                if (outSigRevisionNumber != cmpSigRevisionNumber)
                {
                    AddComparisonError("Signature revision number", outSigRevisionNumber.ToString(),
                        cmpSigRevisionNumber.ToString());
                }

                String outTimeStampService = outSig.GetTimeStampService();
                String cmpTimeStampService = cmpSig.GetTimeStampService();
                if (CheckIfEqual(outTimeStampService, cmpTimeStampService))
                {
                    AddComparisonError("TimeStamp service", outTimeStampService, cmpTimeStampService);
                }

                bool outAnnotsAllowed = outSig.IsAddingAnnotationsAllowed();
                bool cmpAnnotsAllowed = cmpSig.IsAddingAnnotationsAllowed();
                if (outAnnotsAllowed != cmpAnnotsAllowed)
                {
                    AddComparisonError("Annotations allowance", outAnnotsAllowed.ToString(), cmpAnnotsAllowed
                        .ToString());
                }

                bool outFieldsFillAllowed = outSig.IsFieldsFillAllowed();
                bool cmpFieldsFillAllowed = cmpSig.IsFieldsFillAllowed();
                if (outFieldsFillAllowed != cmpFieldsFillAllowed)
                {
                    AddComparisonError("Fields filling allowance", outFieldsFillAllowed.ToString(), cmpFieldsFillAllowed
                        .ToString());
                }

                bool outIsCertification = outSig.IsCertifiaction();
                bool cmpIsCertification = cmpSig.IsCertifiaction();
                if (outIsCertification != cmpIsCertification)
                {
                    AddComparisonError("Comparing signature to certification result", outIsCertification
                        .ToString(), cmpIsCertification.ToString());
                }

                bool outIsWholeDocument = outSig.IsSignatureCoversWholeDocument();
                bool cmpIsWholeDocument = cmpSig.IsSignatureCoversWholeDocument();
                if (outIsWholeDocument != cmpIsWholeDocument)
                {
                    AddComparisonError("Whole document covering", outIsWholeDocument.ToString(), cmpIsWholeDocument
                        .ToString());
                }

                Rectangle outFp = outSig.GetSignaturePosition();
                Rectangle cmpFp = cmpSig.GetSignaturePosition();
                String outPositionRect = outFp == null ? null : outFp.ToString();
                String cmpPositionRect = cmpFp == null ? null : cmpFp.ToString();
                if (CheckIfEqual(outPositionRect, cmpPositionRect))
                {
                    AddComparisonError("Signature position", outPositionRect, cmpPositionRect);
                }

                IList<String> outFieldLocks = outSig.GetFieldsLocks();
                IList<String> cmpFieldLocks = cmpSig.GetFieldsLocks();
                int outLocksNumber = outFieldLocks.Count;
                int cmpLocksNumber = cmpFieldLocks.Count;
                if (outLocksNumber != cmpLocksNumber)
                {
                    AddComparisonError("Field locks number", outLocksNumber.ToString(), cmpLocksNumber
                        .ToString());
                }

                for (int j = 0; j < outLocksNumber; ++j)
                {
                    if (!outFieldLocks[j].Equals(cmpFieldLocks[j]))
                    {
                        AddComparisonError("Field lock", outFieldLocks[j], cmpFieldLocks[j]);
                    }
                }

                if (outSig.GetCertificateInfos().Count != cmpSig.GetCertificateInfos().Count)
                {
                    AddComparisonError("Certificates number", outSig.GetCertificateInfos().Count.ToString(), 
                        cmpSig.GetCertificateInfos().Count.ToString());
                }

                for (int j_1 = 0; j_1 < outSig.GetCertificateInfos().Count; ++j_1)
                {
                    CertificateInfo outCert = outSig.GetCertificateInfos()[j_1];
                    CertificateInfo cmpCert = cmpSig.GetCertificateInfos()[j_1];
                    if (!outCert.GetIssuer().Equals(cmpCert.GetIssuer()))
                    {
                        AddComparisonError("Certificate issuer", outCert.GetIssuer().ToString(),
                            cmpCert.GetIssuer().ToString());
                    }

                    if (!outCert.GetSubject().Equals(cmpCert.GetSubject()))
                    {
                        AddComparisonError("Certificate subject", outCert.GetSubject().ToString(), cmpCert
                            .GetSubject().ToString());
                    }

                    if (!outCert.GetValidFrom().Equals(cmpCert.GetValidFrom()))
                    {
                        AddComparisonError("Date \"valid from\"", outCert.GetValidFrom().ToString(), cmpCert
                            .GetValidFrom().ToString());
                    }

                    if (!outCert.GetValidTo().Equals(cmpCert.GetValidTo()))
                    {
                        AddComparisonError("Date \"valid to\"", outCert.GetValidTo().ToString(),
                            cmpCert.GetValidTo().ToString());
                    }
                }
            }
        }

        private bool CheckIfEqual(Object obj1, Object obj2)
        {
            return !CheckNulls(obj1, obj2) || (obj1 != null && !obj1.Equals(obj2));
        }

        private bool CheckNulls(Object obj1, Object obj2)
        {
            return (obj1 == null && obj2 == null) || (obj1 != null && obj2 != null);
        }

        private void AddComparisonError(String comparisonCategory, String newVal, String oldVal)
        {
            String error = "{0} [{1}] isn't equal to expected value [{2}].";
            AddError(String.Format(error, comparisonCategory, newVal, oldVal));
        }

        private void AddError(String error)
        {
            if (error != null && error.Length > 0)
            {
                if (errorMessage == null)
                {
                    errorMessage = "";
                }
                else
                {
                    errorMessage += "\n";
                }

                errorMessage += error;
            }
        }
    }
}