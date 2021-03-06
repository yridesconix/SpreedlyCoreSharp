﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using SpreedlyCoreSharp.Domain;
using SpreedlyCoreSharp.Request;
using SpreedlyCoreSharp.Response;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;

namespace SpreedlyCoreSharp
{
    public class CoreService : ICoreService
    {
        private const string BaseUrl = "https://core.spreedly.com";
        private const string GatewaysUrl = "/v1/gateways.xml";
        private const string RedactGatewayUrl = "/v1/gateways/{0}/redact.xml";
        private const string ProcessPaymentUrl = "/v1/gateways/{0}/purchase.xml";
        private const string TransactionsUrl = "/v1/transactions.xml";
        private const string TransactionUrl = "/v1/transactions/{0}.xml";
        private const string PaymentMethodUrl = "/v1/payment_methods/{0}.xml";
        private const string PaymentMethodRetainUrl = "/v1/payment_methods/{0}/retain.xml";
        private const string TransactionTranscriptUrl = "/v1/transactions/{0}/transcript";

        private readonly HttpClient _client;

        private readonly string _apiEnvironment;
        private readonly string _apiSecret;
        private readonly string _apiSigningSecret;
        private readonly string _gatewayToken;

        public string APIEnvironment { get { return _apiEnvironment; } }
        public string APISecret { get { return _apiSecret; } }
        public string APISigningSecret { get { return _apiSigningSecret; } }
        public string GatewayToken { get { return _gatewayToken; } }

        public CoreService(string apiEnvironment, string apiSecret, string apiSigningSecret, string gatewayToken)
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _apiEnvironment = apiEnvironment;
            _apiSecret = apiSecret;
            _apiSigningSecret = apiSigningSecret;
            _gatewayToken = gatewayToken;

            var handler = new HttpClientHandler();
            handler.Credentials = new NetworkCredential(_apiEnvironment, _apiSecret);

            _client = new HttpClient(handler, true);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        }

        /// <summary>
        /// Exposes a method to deserialize a group of transactions, used mainly in
        /// the 3D secure call back functionality
        /// </summary>
        /// <param name="xml">raw transactions xml</param>
        /// <returns></returns>
        public IEnumerable<Transaction> DeserializeTransactions(string xml)
        {
            var doc = new XmlDocument();

            doc.LoadXml(xml);

            if (doc.DocumentElement == null) yield break;

            var nodes = doc.DocumentElement.SelectNodes("transaction");

            if (nodes == null) yield break;

            foreach (XmlNode node in nodes)
            {
                yield return Deserialize<Transaction>(node.OuterXml);
            }
        }

        /// <summary>
        /// Turns an XML string into T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="xml"></param>
        /// <returns></returns>
        public T Deserialize<T>(string xml)
        {
            var stream = new MemoryStream(Encoding.ASCII.GetBytes(xml));

            var serializer = new XmlSerializer(typeof(T));

            var obj = (T)serializer.Deserialize(stream);

            if (typeof(T) == typeof(Transaction))
            {
                obj.GetType().GetProperty("RawTransactionXml").SetValue(obj, xml);
            }

            return obj;
        }

        /// <summary>
        /// Turns T into an XML string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public string Serialize<T>(object item)
        {
            var serializer = new XmlSerializer(typeof(T));
            var ns = new XmlSerializerNamespaces();
            ns.Add("", "");

            var stream = new MemoryStream();

            serializer.Serialize(stream, item, ns);

            stream.Position = 0;

            return new StreamReader(stream).ReadToEnd();
        }

        /// <summary>
        /// Adds a gateway
        /// </summary>
        /// <param name="gatewayRequest">gateway request object</param>
        /// <returns></returns>
        public Gateway AddGateway(object gatewayRequest)
        {
            var content=new StringContent(gatewayRequest.ToString());
            var result = _client.PostAsync(BaseUrl + GatewaysUrl, content).Result;            
            var resultContent=result.Content.ReadAsStringAsync().Result;            

            return Deserialize<Gateway>(resultContent);
        }

        /// <summary>
        /// Redacts a gateway, this is permanent.
        /// </summary>
        /// <param name="gatewayToken">token of gateway</param>
        public void RedactGateway(string gatewayToken)
        {
            // TODO: do something with response?
            var content = new StringContent("");
            var result = _client.PutAsync(BaseUrl + string.Format(RedactGatewayUrl, gatewayToken), content).Result;
        }

        /// <summary>
        /// Fetches a list of gateways
        /// </summary>
        /// <returns></returns>
        public List<Gateway> GetGateways()
        {
            var result = _client.GetStringAsync(BaseUrl + GatewaysUrl).Result;

            var gateways = Deserialize<GetGatewaysResponse>(result);

            return gateways.Gateways;
        }

        /// <summary>
        /// Fetches a single transaction
        /// </summary>
        /// <param name="token">token of transaction</param>
        /// <returns></returns>
        public Transaction GetTransaction(string token)
        {
            string url = BaseUrl + string.Format(TransactionUrl, token);

            var resultText= _client.GetStringAsync(url).Result;

            return Deserialize<Transaction>(resultText);
        }

        /// <summary>
        /// Fetches a payment method
        /// </summary>
        /// <param name="token">token of payment method</param>
        /// <returns></returns>
        public PaymentMethod GetPaymentMethod(string token)
        {
            string url = BaseUrl + string.Format(PaymentMethodUrl, token);

            var resultText = _client.GetStringAsync(url).Result;

            return Deserialize<PaymentMethod>(resultText);
        }

        /// <summary>
        /// Retains a payment method
        /// </summary>
        /// <param name="token">token payment method</param>
        /// <returns></returns>
        public Transaction RetainPaymentMethod(string token)
        {
            string url = BaseUrl + string.Format(PaymentMethodRetainUrl, token);

            var response = _client.PutAsync(url, null).Result;
            var resultText = response.Content.ReadAsStringAsync().Result;

            return Deserialize<Transaction>(resultText);
        }

        /// <summary>
        /// Fetches a list of transactions
        /// </summary>
        /// <param name="sinceToken">token of transaction to start from</param>
        /// <returns></returns>
        public List<Transaction> GetTransactions(string sinceToken = "")
        {
            string url;

            if (!string.IsNullOrWhiteSpace(sinceToken))
            {
                url = string.Format("{0}{1}?since_token={2}", BaseUrl, TransactionsUrl, sinceToken);
            }
            else
            {
                url = string.Format("{0}{1}", BaseUrl, TransactionsUrl);
            }

            var resultText = _client.GetStringAsync(url).Result;

            var transactions = Deserialize<GetTransactionsResponse>(resultText);

            return transactions.Transactions;
        }

        /// <summary>
        /// Fetches a transaction raw transaction
        /// This will be empty for test gateway transactions
        /// </summary>
        /// <param name="token">token of transaction</param>
        /// <returns></returns>
        public string GetTransactionTranscript(string token)
        {
            string url = BaseUrl + string.Format(TransactionTranscriptUrl, token);

            var resultText = _client.GetStringAsync(url).Result;

            return resultText;
        }

        /// <summary>
        /// Sends a purchase request to the active gateway
        /// </summary>
        /// <param name="request">purchase request</param>
        /// <returns></returns>
        public Transaction ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var data = this.Serialize<ProcessPaymentRequest>(processPaymentRequest);
            var content = new StringContent(data, Encoding.UTF8, "application/xml");
            var url = BaseUrl + string.Format(ProcessPaymentUrl, _gatewayToken);
            
            var client = new WebClient();
            client.Credentials = new NetworkCredential(_apiEnvironment, _apiSecret);
            client.Headers.Add(HttpRequestHeader.ContentType, "application/xml");

            string resultText = "";
            try
            {
                resultText = client.UploadString(url, "POST", data);
            }
            catch (WebException wex)
            {
                var response = wex.Response.GetResponseStream();
                var reader= new StreamReader(response);
                resultText = reader.ReadToEnd();

                var transaction = this.Deserialize<Transaction>(resultText);

                transaction.TransactionResponse.ErrorDetail = resultText;

                return transaction;
            }

            //var response = _client.PostAsync(url, content).Result;
            //var resultText = response.Content.ReadAsStringAsync().Result;
            
            if (processPaymentRequest.Attempt3DSecure && string.IsNullOrWhiteSpace(processPaymentRequest.CallbackUrl))
            {
                throw new ArgumentException("Callback URL cannot be empty.");
            }

            if (processPaymentRequest.Attempt3DSecure && string.IsNullOrWhiteSpace(processPaymentRequest.RedirectUrl))
            {
                throw new ArgumentException("Redirect URL cannot be empty.");
            }

            // Seems if you send absolutely nothing it decides to return <errors> rather than full <transaction> doc...
            // Not sure how to append this to a Transaction document.
            if (resultText.StartsWith("<errors>"))
            {
                var errors = Deserialize<TransactionErrors>(resultText);

                return new Transaction
                {
                    TransactionResponse = new Transaction.Response
                    {                       
                        ErrorDetail=resultText,
                        Errors=errors,
                        Success = false
                    },                    
                };
            }
            else
            {
                return Deserialize<Transaction>(resultText);
            }
        }

        /// <summary>
        /// Validates a transaction with a signature
        /// </summary>
        /// <param name="transaction">Transaction</param>
        /// <returns></returns>
        public bool ValidateTransactionSignature(Transaction transaction)
        {
            return ValidateTransactionSignature(transaction.RawTransactionXml);
        }

        /// <summary>
        /// Validates a transaction with a signature
        /// </summary>
        /// <param name="transactionXml">Transaction xml</param>
        /// <returns></returns>
        public bool ValidateTransactionSignature(string transactionXml)
        {
            // Translation of ruby code to C# from manual:
            // https://core.spreedly.com/manual/signing

            var doc = new XmlDocument();

            doc.LoadXml(transactionXml);

            // If xml is empty return false, something is wrong
            if (doc.DocumentElement == null)
            {
                return false;
            }

            var signedNode = doc.DocumentElement.SelectSingleNode("signed");

            // If xml doesn't have a signed element return false.
            // Undecided if this is final behaviour as might cause problems when
            // transactions don't have a signed xml node
            if (signedNode == null)
            {
                return false;
            }

            var signatureNode = signedNode.SelectSingleNode("signature");
            var signature = "";

            if (signatureNode != null)
            {
                signature = signatureNode.InnerText;
            }

            // Check we know what algorithm they are using, sample data indicated SHA1 only
            var algorithmNode = signedNode.SelectSingleNode("algorithm");
            var algorithm = "sha1";

            if (algorithmNode != null)
            {
                algorithm = algorithmNode.InnerText;
            }

            if (algorithm.Trim().ToUpper() != "SHA1")
            {
                throw new ArgumentException("Unknown transaction signature algorithm.");
            }

            var fieldsMushed = "";
            var signedFields = "";
            var signedFieldsNode = signedNode.SelectSingleNode("fields");

            if (signedFieldsNode != null)
            {
                signedFields = signedFieldsNode.InnerText;
            }

            foreach (var item in signedFields.Split(' '))
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                var node = doc.DocumentElement.SelectSingleNode(item);

                if (node != null)
                {
                    fieldsMushed += node.InnerText + "|"; 
                }
            }

            fieldsMushed = fieldsMushed.Substring(0, fieldsMushed.Length - 1);

            var myhmacsha1 = new HMACSHA1(Encoding.ASCII.GetBytes(APISigningSecret));

            var byteArray = Encoding.ASCII.GetBytes(fieldsMushed);

            var stream = new MemoryStream(byteArray);

            var result = myhmacsha1.ComputeHash(stream).Aggregate("", (s, e) => s + String.Format("{0:x2}", e), s => s);

            return result == signature;
        }
    }
}
