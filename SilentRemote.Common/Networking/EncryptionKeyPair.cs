using System.Security.Cryptography;

namespace SilentRemote.Common.Networking
{
    /// <summary>
    /// Represents a pair of encryption keys for secure communication
    /// </summary>
    public class EncryptionKeyPair
    {
        /// <summary>
        /// The public key used for encryption
        /// </summary>
        public byte[] PublicKey { get; private set; }
        
        /// <summary>
        /// The private key used for decryption (only known to owner)
        /// </summary>
        public byte[] PrivateKey { get; private set; }
        
        /// <summary>
        /// Creates a new encryption key pair
        /// </summary>
        public EncryptionKeyPair(byte[] publicKey, byte[] privateKey)
        {
            PublicKey = publicKey;
            PrivateKey = privateKey;
        }
        
        /// <summary>
        /// Generates a new encryption key pair
        /// </summary>
        /// <returns>A newly generated encryption key pair</returns>
        public static EncryptionKeyPair Generate()
        {
            using (var rsa = RSA.Create(2048))
            {
                // Export the public key
                var publicKey = rsa.ExportRSAPublicKey();
                
                // Export the private key
                var privateKey = rsa.ExportRSAPrivateKey();
                
                return new EncryptionKeyPair(publicKey, privateKey);
            }
        }
        
        /// <summary>
        /// Encrypt data using the public key
        /// </summary>
        /// <param name="data">Data to encrypt</param>
        /// <returns>Encrypted data</returns>
        public byte[] Encrypt(byte[] data)
        {
            using (var rsa = RSA.Create(2048))
            {
                rsa.ImportRSAPublicKey(PublicKey, out _);
                return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            }
        }
        
        /// <summary>
        /// Decrypt data using the private key
        /// </summary>
        /// <param name="encryptedData">Encrypted data</param>
        /// <returns>Decrypted data</returns>
        public byte[] Decrypt(byte[] encryptedData)
        {
            using (var rsa = RSA.Create(2048))
            {
                rsa.ImportRSAPrivateKey(PrivateKey, out _);
                return rsa.Decrypt(encryptedData, RSAEncryptionPadding.OaepSHA256);
            }
        }
    }
}
