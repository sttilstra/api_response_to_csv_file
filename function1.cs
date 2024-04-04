using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Data.SqlClient;
using CsvHeProjecter;
using RestSharp;
using RestSharp.Authenticators;
using System.Globalization;
using Azure.Storage.Blobs;
using System.Collections.Generic;
using System.Linq;


namespace Project_Azure_Function
{
    public static class Function1
    {
        [FunctionName("Project")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var API_USERNAME = "<username>";
            var API_PASSWORD = Environment.GetEnvironmentVariable("API_PASSWORD");


            // gets current datetime for use with filename output.
            DateTime utc_time = DateTime.UtcNow;
            TimeZoneInfo eastern_zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime eastern_time = TimeZoneInfo.ConvertTimeFromUtc(utc_time, eastern_zone);

            string current_time = eastern_time.ToString("h:mm:ss tt");
        


            // Gets the bid internal id and revision number parameter values from the incoming flow http request
            var internal_id = req.Query["bidInternalID"];
            var revision_number = req.Query["revisionNumber"];

            log.LogInformation(internal_id);
            log.LogInformation(revision_number);

            string API_ENDPOINT;

            if (revision_number == "")
                API_ENDPOINT = $"<endpoint>";
            else
                API_ENDPOINT = $"<endpoint>" + $"&revisionNumber={revision_number}";


            log.LogInformation(API_ENDPOINT);

            var client = new RestClient(API_ENDPOINT);
            client.Authenticator = new HttpBasicAuthenticator(API_USERNAME, API_PASSWORD);

            var request = new RestRequest();

            var response = client.Execute(request);
          
            log.LogInformation("response status code: " + response.StatusCode.ToString());

            var raw_response = response.Content;

            var bid_response = JsonConvert.DeserializeObject<BidResponse>(raw_response); 

            var list_bids = new List<Bid>();

            var bids = bid_response.Data;
			

            // CREATES LIST FOR PRODCUT CODES AND THEN MAKES A STRING OF THEM WITH QUOTES FOR EACH ITEM TO SEND TO SQL TABLE
            List<string> list_ProdCodes = new List<string>();

            foreach (var bid in bids)
            {
                var prodcode_quotes = bid.ProdCode.Replace(bid.ProdCode, $"'{bid.ProdCode}'");
                list_ProdCodes.Add(prodcode_quotes);
            }

            string ProdCode_string = string.Join(", ", list_ProdCodes);



            ////SECTION
            ////  SQL CONNECTION AND QUERY - ALSO CREATES DICTIONARY OF ProdCode/Alt PRODUCT Code and ProdCode/VENDOR PROD CODE

            var dict_alt = new Dictionary<string, string>();

            var dict_vendor_code = new Dictionary<string, string>();

            var sql_password = Environment.GetEnvironmentVariable("SQL_PASSWORD");

            SqlConnection conn;
            var connection_string = @$"<connstring>";

            conn = new SqlConnection(connection_string);
            conn.Open();
            log.LogInformation("The sql connection opened successfully!");


            SqlCommand command;
            SqlDataReader dataReader;

            String sql, Output = "";

            sql = $"<sql query>";

            command = new SqlCommand(sql, conn);

            dataReader = command.ExecuteReader();

            while (dataReader.Read())
            {
                dict_alt.Add(dataReader.GetString(0), dataReader.GetString(1));
                dict_vendor_code.Add(dataReader.GetString(0), dataReader.GetString(2));
                Output = Output + dataReader.GetValue(0) + " - " + dataReader.GetValue(2) + "\n";
            }


            dataReader.Close();
            command.Dispose();
            conn.Close();

            log.LogInformation("The sql connection closed successfully!");


            // THIS ADDS THE API BID RESPONSE ITEMS TO THE BID LIST AND LOOPS THROUGH THE ALT CODE/MPN DICTIONARY TO GET THE ALT PROD NUMBER FOR THE LINK TEMPLATE
            foreach (var bid in bids)
            {
                bid.SpecSheet = "<url>";
                bid.ProductLink = "<url>";
                bid.ProductImage = "<url>;
                bid.VendorProductCode = "";


                //Removing line breaks from product description - multi line comments mess up excel --- bidtracer is sending stuff over weird.
                bid.Description = bid.Description.Replace("\n", "****").Replace("\r", "****");
                bid.Description = bid.Description.Replace("****", "");


                foreach (var item in dict_alt)
                {

                    if (item.Key == bid.ProdCode)
                    {
                        bid.SpecSheet = $"<url>";
                        bid.ProductLink = $"<url>";
                        bid.ProductImage = $"<url>;
                    }

                }

                foreach (var item in dict_vendor_code)
                {
                    if (item.Key == bid.ProdCode)
                        bid.VendorProductCode = item.Value;
                }


                list_bids.Add(bid);
            }




            ////SECTION
            ////CREATE A CSV FILE AND UPLOAD IT TO BLOB STORAGE

            //// create AZ client

            var account_key = Environment.GetEnvironmentVariable("ACCOUNT_KEY");
            var storage_account_name = "<name>";
            var container_name = "<container name>";
            var az_connection_string = $@"<connstring>;

            var container_client = new BlobContainerClient(az_connection_string, container_name);

            var file_name = $"{bids.First().BidID} " + current_time + ".csv";

            var blob_client = container_client.GetBlobClient(file_name);


            //// this creates a csv in memory and then uploads it to the blob container - combo of streamwriter/csv and memory stream
            var memory_stream = new MemoryStream();
            var writer = new StreamWriter(memory_stream);
            var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(list_bids);

            csv.Flush();
            writer.Flush();
            memory_stream.Position = 0;
            blob_client.Upload(memory_stream);

            csv.Dispose();
            writer.Dispose();
            memory_stream.Dispose();


            log.LogInformation("File uploaded to blob storage!!");


            return new OkObjectResult(file_name);
        }


        public class Bid
        {
            public string BidID { get; set; }

            [JsonProperty("Project Name")]
            public string ProjectName { get; set; }
            public int BidInternalID { get; set; }
            public double RevisionNumber { get; set; }
            public string? SubmittalGroup { get; set; }
            public string? ProdCode { get; set; }
            public string Description { get; set; }
            public string ProductImage { get; set; }
            public string Manufacturer { get; set; }
            public string VendorProductCode { get; set; }
            public double Cost { get; set; }
            public double PricePerUnit { get; set; }
            public double Quantity { get; set; }
            public string ProductLink { get; set; }
            public string SpecSheet { get; set; }


        }

        public class BidResponse
        {
            public bool IsSuccess { get; set; }
            public string Message { get; set; }
            public List<Bid> Data { get; set; }
        }
    }
}
