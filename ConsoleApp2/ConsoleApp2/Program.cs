using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using CsvHelper;
using Newtonsoft.Json.Linq;
using System.Text.Json;

class Program
{
    private static readonly string accessToken = "UKnEiJPmNpU-ttKDV39SyWT5_H2Z_agzMpuuWRvxx5HWI7wRWSQsmQUCJ9sgGeJQo9OdTqpXk9ik0Dg2ilDBlA";
    private static readonly string baseUrl = "https://api.mypurecloud.com/api/v2";

    static async Task Main()
    {
        string csvFile = "genesys_users.csv";

        using (var writer = new StreamWriter(csvFile))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteField("User ID");
            csv.WriteField("Name");
            csv.WriteField("Division");
            csv.WriteField("Email");
            csv.WriteField("Auto Answer");
            csv.WriteField("Queues");
            csv.WriteField("Groups");
            csv.WriteField("Roles");
            csv.WriteField("Licenses");
            csv.WriteField("StationDetails"); 
            csv.NextRecord();

            JArray users = await GetJsonArrayFromApi("/users");

            foreach (JObject user in users)
            {
                string userId = user["id"]?.ToString() ?? "";
                string name = user["name"]?.ToString() ?? "";
                string division = user["division"]?["name"]?.ToString() ?? "";
                string email = user["email"]?.ToString() ?? "";
                string autoAnswer = user["acdAutoAnswer"]?.ToString() ?? "";
                string queues = await GetUserQueues(userId);
                string groups = await GetUserGroupNames(userId);
                string roles = await GetUserRoles(userId);
                string licenses = await GetUserLicenses(userId);
                string stationId = await GetUserStationId(userId);
                string stationDetails = stationId != "N/A" ? await GetStationDetails(stationId) : "N/A";

                csv.WriteField(userId);
                csv.WriteField(name);
                csv.WriteField(division);
                csv.WriteField(email);
                csv.WriteField(autoAnswer);
                csv.WriteField(queues);
                csv.WriteField(groups);
                csv.WriteField(roles);
                csv.WriteField(licenses);
                csv.WriteField(stationDetails);
                csv.NextRecord();
            }
        }

        Console.WriteLine("CSV generado exitosamente.");
    }

    static async Task<JArray> GetJsonArrayFromApi(string endpoint)
    {
        JArray allEntities = new JArray();
        int pageNumber = 1;
        int pageSize = 100;  // Ajusta según la documentación de Genesys

        while (true)
        {
            string response = await SendGetRequest($"{baseUrl}{endpoint}?pageNumber={pageNumber}&pageSize={pageSize}");
            if (response == null) break;

            JObject jsonResponse = JObject.Parse(response);
            JArray entities = jsonResponse["entities"] as JArray ?? new JArray();

            if (entities.Count == 0) break;  // No hay más datos

            allEntities.Merge(entities);
            pageNumber++;
        }

        return allEntities;
    }


    static async Task<string> GetUserQueues(string userId)
    {
        JArray queues = await GetJsonArrayFromApi($"/users/{userId}/queues");
        List<string> queueNames = new List<string>();

        foreach (JObject queue in queues)
        {
            queueNames.Add(queue["name"]?.ToString() ?? "");
        }

        return string.Join(", ", queueNames);
    }

    private static async Task<string> GetUserGroupNames(string userId)
    {
        JsonDocument userDetails = await GetJsonObjectFromApi($"/users/{userId}?expand=groups");
        //Console.WriteLine($"User Details (Groups): {userDetails.RootElement.ToString()}"); // Depuración

        if (userDetails.RootElement.TryGetProperty("groups", out JsonElement groupsArray))
        {
            List<string> groupNames = new List<string>();
            foreach (JsonElement groupElement in groupsArray.EnumerateArray())
            {
                string groupId = groupElement.GetProperty("id").GetString();
                JsonDocument groupDetails = await GetJsonObjectFromApi($"/groups/{groupId}");
                if (groupDetails.RootElement.TryGetProperty("name", out JsonElement groupName))
                {
                    groupNames.Add(groupName.GetString());
                }
            }
            return groupNames.Count > 0 ? string.Join(", ", groupNames) : "N/A";
        }
        return "N/A";
    }

    private static async Task<string> GetUserRoles(string userId)
    {
        JsonDocument userDetails = await GetJsonObjectFromApi($"/users/{userId}?expand=authorization");
        //Console.WriteLine($"User Details (Roles): {userDetails.RootElement.ToString()}"); // Depuración

        if (userDetails.RootElement.TryGetProperty("authorization", out JsonElement authorization) &&
            authorization.TryGetProperty("roles", out JsonElement rolesArray))
        {
            List<string> roles = new List<string>();
            foreach (JsonElement role in rolesArray.EnumerateArray())
            {
                roles.Add(role.GetProperty("name").GetString());
            }
            return roles.Count > 0 ? string.Join(", ", roles) : "N/A";
        }
        return "N/A";
    }

    private static async Task<string> GetUserLicenses(string userId)
    {
        JsonDocument licenseData = await GetJsonObjectFromApi("/license/users");
        //Console.WriteLine($"License Data: {licenseData.RootElement.ToString()}"); // Depuración

        if (licenseData.RootElement.TryGetProperty("entities", out JsonElement usersArray))
        {
            foreach (JsonElement user in usersArray.EnumerateArray())
            {
                if (user.GetProperty("id").GetString() == userId && user.TryGetProperty("licenses", out JsonElement licensesArray))
                {
                    List<string> licenses = new List<string>();
                    foreach (JsonElement license in licensesArray.EnumerateArray())
                    {
                        licenses.Add(license.GetString());
                    }
                    return licenses.Count > 0 ? string.Join(", ", licenses) : "N/A";
                }
            }
        }
        return "N/A";
    }


    static async Task<string> GetUserStationId(string userId)
    {
        JsonDocument stationData = await GetJsonObjectFromApi($"/users/{userId}/station");

        if (stationData == null)
        {
            Console.WriteLine($"[ERROR] No se pudo obtener la estación para el usuario {userId}");
            return "N/A";
        }

        Console.WriteLine($"[DEBUG] Respuesta de /users/{userId}/station: {stationData.RootElement}");

        if (!stationData.RootElement.TryGetProperty("associatedStation", out JsonElement associatedStation) ||
            !associatedStation.TryGetProperty("id", out JsonElement stationIdElement))
        {
            return "N/A";
        }

        return stationIdElement.GetString();
    }

    static async Task<string> GetStationDetails(string stationId)
    {
        JsonDocument stationData = await GetJsonObjectFromApi($"/stations/{stationId}");

        if (stationData == null)
        {
            Console.WriteLine($"[ERROR] No se pudo obtener detalles para la estación {stationId}");
            return "N/A";
        }

        Console.WriteLine($"[DEBUG] Respuesta de /stations/{stationId}: {stationData.RootElement}");

        string status = stationData.RootElement.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString() : "N/A";
        string primaryEdge = stationData.RootElement.TryGetProperty("primaryEdge", out JsonElement primaryEdgeElement) &&
                             primaryEdgeElement.TryGetProperty("name", out JsonElement primaryEdgeName) ? primaryEdgeName.GetString() : "N/A";
        string secondaryEdge = stationData.RootElement.TryGetProperty("secondaryEdge", out JsonElement secondaryEdgeElement) &&
                               secondaryEdgeElement.TryGetProperty("name", out JsonElement secondaryEdgeName) ? secondaryEdgeName.GetString() : "N/A";
        string stationType = stationData.RootElement.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : "N/A";
        string webRtcPersistentEnabled = stationData.RootElement.TryGetProperty("webRtcPersistentEnabled", out JsonElement webRtcElement) ? webRtcElement.GetBoolean().ToString() : "N/A";
        string webRtcRequireMediaHelper = stationData.RootElement.TryGetProperty("webRtcRequireMediaHelper", out JsonElement webRtcHelperElement) ? webRtcHelperElement.GetBoolean().ToString() : "N/A";

        return $"{status} | {primaryEdge} | {secondaryEdge} | {stationType} | {webRtcPersistentEnabled} | {webRtcRequireMediaHelper}";
    }


    static async Task<JsonDocument> GetJsonObjectFromApi(string endpoint, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            string response = await SendGetRequest($"{baseUrl}{endpoint}");
            if (!string.IsNullOrEmpty(response))
            {
                return JsonDocument.Parse(response);
            }
            await Task.Delay(500);  // Espera 500ms antes de reintentar
        }

        Console.WriteLine($"[ERROR] No se pudo obtener datos de {endpoint} después de {retries} intentos.");
        return null;
    }

    private static readonly SemaphoreSlim apiSemaphore = new SemaphoreSlim(5);  // Máximo 5 peticiones a la vez

    static async Task<string> SendGetRequest(string url)
    {
        await apiSemaphore.WaitAsync();
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                Console.WriteLine($"[ERROR] Fallo en la petición a {url} - Código: {response.StatusCode}");
                return null;
            }
        }
        finally
        {
            apiSemaphore.Release();
        }
    }

}
