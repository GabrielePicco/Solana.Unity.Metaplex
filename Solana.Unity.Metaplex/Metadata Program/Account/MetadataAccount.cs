using Newtonsoft.Json;
using Solana.Unity.Metaplex.Utilities;
using Solana.Unity.Metaplex.Utilities.Json;
using Solana.Unity.Programs.Utilities;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Core.Http;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Solana.Unity.Metaplex.NFT.Library
{

    /// <summary> Metaplex Metadata Account Class </summary>
    public class MetadataAccount
    {
        /// <summary> metadata public key </summary>
        public PublicKey metadataKey;

        /// <summary> update authority key </summary>
        public PublicKey updateAuthority;

        /// <summary> mint public key </summary>
        public string mint;
        /// <summary> data struct </summary>
        public OnChainData metadata;

        /// <summary> Off Chain Metadata </summary>
        public MetaplexTokenStandard offchainData;

        /// <summary> standard Solana account info </summary>
        public AccountInfo accInfo;

        /// <summary> owner, should be Metadata program</summary>
        public PublicKey owner;

        private MetadataAccount()
        {
            
        }

        /// <summary>
        /// Constructor to build a MetadataAccount from a Solana AccountInfo
        /// </summary>
        /// <param name="accInfo"></param>
        /// <returns></returns>
        public static async Task<MetadataAccount> BuildMetadataAccount(AccountInfo accInfo)
        {
            var pkMint = PublicKey.DefaultPublicKey;
            try
            {
                var met = ParseData(accInfo.Data);
                
                
                byte[] data = Convert.FromBase64String(accInfo.Data[0]);
                
                var updateAuthority = new ArraySegment<byte>( data, 1, 32).ToArray();
                var mint = new ArraySegment<byte>( data, 33, 32).ToArray();
                pkMint = new PublicKey(mint);

                var metadata = new MetadataAccount()
                {
                    metadata = met,
                    offchainData = await FetchOffChainMetadata(met.uri),
                    owner = new PublicKey(accInfo.Owner),
                    updateAuthority = new PublicKey(updateAuthority),
                    mint = pkMint
                };
                return metadata;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading nft: {pkMint}", ex);
            }

            return null;
        }

        /// <summary> Tries to get a json file from the uri </summary>
        public static async Task<MetaplexTokenStandard> FetchOffChainMetadata(string URI)
        {
            MetaplexTokenStandard _Metadata = null;
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:47.0) Gecko/20100101 Firefox/47.0");
                var response = await CrossHttpClient.SendAsyncRequest(httpClient, new HttpRequestMessage(HttpMethod.Get, URI));
                if(response == null) throw new Exception("Response is null");
                var responseContent = await response.Content.ReadAsStringAsync();
                if(response.StatusCode != HttpStatusCode.OK) throw new Exception(responseContent);
                _Metadata = JsonConvert.DeserializeObject<MetaplexTokenStandard>(responseContent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            return _Metadata;
        }

        /// <summary> Parse raw data used to propagate the metadata account class</summary>
        /// <param name="data"> data </param>
        /// <returns> data struct </returns>
        /// <remarks> parses an array of bytes into a data struct </remarks>
        public static OnChainData ParseData(List<string> data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(data[0]);
                ReadOnlySpan<byte> binData = new(bytes);


                int nameLength = binData.GetBorshString(MetadataPacketLayout.nameOffset, out string name);
                int symbolLength = binData.GetBorshString(MetadataPacketLayout.symbolOffset, out string symbol);
                int uriLength = binData.GetBorshString(MetadataPacketLayout.uriOffset, out string uri);
                uint sellerFee = binData.GetU16(MetadataPacketLayout.feeBasisOffset);

                //bool hasCreators = binData.GetBool(MetadataPacketLayout.creatorSwitchOffset);
                int numOfCreators = binData.GetU8(MetadataPacketLayout.creatorsCountOffset);

                IList<Creator> creators = null;
                Uses usesInfo = null;
                Collection collectionLink = null;
                ProgrammableConfig programmableconfig = null;
                int o = 0;
                bool hasCreators = !(binData.Length < MetadataPacketLayout.creatorsCountOffset + 5 + numOfCreators * (32 + 1 + 1));

                if (hasCreators == true)
                {
                    creators = MetadataProgramData.DecodeCreators(binData.GetSpan(MetadataPacketLayout.creatorsCountOffset + 4, numOfCreators * (32 + 2)));
                    o = MetadataPacketLayout.creatorsCountOffset + 4 + numOfCreators * (32 + 2);

                }
                else
                {
                    o = MetadataPacketLayout.creatorSwitchOffset;
                    o++;
                }
                bool primarySaleHappened = binData.GetBool(o);
                o++;
                bool isMutable = binData.GetBool(o);
                o++;
                o++;
                byte editionNonce = binData.GetU8(o);
                o++;
                byte tokenStandard = binData.GetU8(o);
                if ((o + 8) <= binData.Length)
                {
                    o++;
                    o++;
                    bool hasCollectionlink = binData.GetBool(o);
                    o++;

                    if (hasCollectionlink)
                    {
                        var verified = binData.GetBool(o);
                        o++;
                        var key = binData.GetPubKey(o);
                        o += 32;

                        collectionLink = new Collection(key, verified);
                    }
                    else
                    {
                        o++;
                    }

                    bool isConsumable = binData.GetBool(o);
                    if (isConsumable)
                    {
                        o++;
                        byte useMethodENUM = binData.GetBytes(o, 1)[0];
                        o++;
                        string remaining = binData.GetU64(o).ToString("x");
                        o += 8;
                        string total = binData.GetU64(o).ToString("x");
                        o += 8;
                        o++;
                        usesInfo = new Uses((UseMethod)useMethodENUM, Convert.ToInt32(remaining), Convert.ToInt32(total));
                    }
                    else
                    {
                        o++;
                    }
                    if ((o + 1) <= binData.Length)
                    {
                        bool isProgrammable = binData.GetBool(o);

                        if (isProgrammable)
                        {
                            o++;
                            PublicKey rulesetAddress = binData.GetPubKey(o);
                            programmableconfig = new ProgrammableConfig(rulesetAddress);
                        }
                    }
                }
                name = name.TrimEnd('\0');
                symbol = symbol.TrimEnd('\0');
                uri = uri.TrimEnd('\0');
                var res = new OnChainData(name, symbol, uri, sellerFee, creators, editionNonce, tokenStandard, collectionLink, usesInfo, programmableconfig, isMutable);

                return res;
            }
            catch (Exception ex)
            {
                throw new Exception("could not decode account data from base64", ex);
            }
        }

        /// <summary>GetAccount Method Retrieves the metadata of a token including both onchain and offchain data</summary>
        /// <param name="client"> solana rpc client </param>
        /// <param name="tokenAddress"> public key of a account to parse </param>
        /// <param name="commitment"></param>
        /// <returns> Metadata account </returns>
        /// <remarks> it will try to find a metadata even from a token associated account </remarks>
        public static async Task<MetadataAccount> GetAccount(IRpcClient client, PublicKey tokenAddress, Commitment commitment = Commitment.Confirmed)
        {
            var accInfoResponse = await client.GetAccountInfoAsync(tokenAddress.Key, commitment);
            if (!accInfoResponse.WasSuccessful) return null;
            AccountInfo accInfo = accInfoResponse.Result.Value;
            if (accInfo == null) return null;
            
            //Account Inception loop to retrieve metadata
            if (accInfo.Owner.Contains("meta"))
            {
                //Triggered after first jump using token account address & metadata address has been retrieved from the first run
                return await BuildMetadataAccount(accInfo);
            }

            //Account Inception first jump - if metadata address doesnt return null
            byte[] rawdata = Convert.FromBase64String(accInfo.Data[0]);
            PublicKey mintAccount;

            if (rawdata.Length == 165)
            {
                byte[] _mint = rawdata.AsSpan(0, 32).ToArray();
                mintAccount = new PublicKey(_mint);
            }
            else
            {
                mintAccount = tokenAddress;
            }

            //Loops back & handles it as a metadata address rather than a token account to retrieve metadata
            return await GetAccount(client, PDALookup.FindMetadataPDA(mintAccount), commitment);
        }
    }
}