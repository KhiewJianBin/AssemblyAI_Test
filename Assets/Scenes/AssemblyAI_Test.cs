using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.IO;

public class AssemblyAI_Test : MonoBehaviour
{
    public InputField statusMessageField;
    public Button RecordBtn;
    public Button PlayRecordBtn;
    public Button UploadAudioBtn;
    public Button TranslateAudioBtn;

    void Awake()
    {
        RecordBtn.onClick.AddListener(OnRecordBtnClick);
        PlayRecordBtn.onClick.AddListener(OnPlayRecordBtnClick);
        UploadAudioBtn.onClick.AddListener(OnUploadAudioBtnClick);
        TranslateAudioBtn.onClick.AddListener(OnTranslateAudioBtnClick);

        statusMessageField.text = "1.Click Record\n2.Upload\n3.Wait\n4.Translate Audio";
    }

    const int MaxAudioClipDuration = 10;
    public AudioSource audioSource;
    string recordingMic;
    void OnRecordBtnClick()
    {
        string[] microphoneDevices = Microphone.devices;
        if(microphoneDevices.Length > 0)
        {
            recordingMic = microphoneDevices[0];
            bool isRecording = Microphone.IsRecording(recordingMic);

            if(!isRecording)
            {
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(recordingMic, out minFreq, out maxFreq);
                audioSource.clip = Microphone.Start(recordingMic, false, MaxAudioClipDuration, minFreq == 0 && maxFreq == 0 ? 44100 : maxFreq);

                //Update UI
                RecordBtn.GetComponentInChildren<Text>().text = "Stop Record";
                statusMessageField.text = "Recording!";
            }
            else
            {
                EndRecording(audioSource, recordingMic);

                //Clear
                recordingMic = "";

                //Update UI
                RecordBtn.GetComponentInChildren<Text>().text = "Start Record";
                statusMessageField.text = "Audio Recorded!";
            }
        }
    }

    void OnPlayRecordBtnClick()
    {
        if (recordingMic != string.Empty && Microphone.IsRecording(recordingMic)) return;

        if (audioSource.clip != null)
        {
            if(!audioSource.isPlaying) audioSource.Play();
        }
    }

    const string APITOKEN = "785d5d7a1ca34f95a3a2d8ed2721de61";
    void OnUploadAudioBtnClick()
    {
        if (recordingMic != string.Empty && Microphone.IsRecording(recordingMic)) return;
        if (audioSource.clip == null) return;

        //Save first
        SavWav.Save("test.wav", audioSource.clip);
        

        //Update UI
        statusMessageField.text = "Uploading - Do not Spam uploads";

        UploadAudio();
    }
    async Task UploadAudio()
    {
        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri("https://api.assemblyai.com/v2/");
        client.DefaultRequestHeaders.Add("authorization", APITOKEN);


        var filepath = Path.Combine(Application.persistentDataPath, "test.wav");

        string jsonResult = await SendFile(client, filepath);
        dynamic jsondata = JsonConvert.DeserializeObject(jsonResult);
        string uploadurl = jsondata.upload_url;

        statusMessageField.text = "Audio Uploaded Completed";

        await ProcessUploadedAudio(uploadurl);

        async Task<string> SendFile(HttpClient client, string filePath)// byte[] filecontent)
        {
            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "upload");
                request.Headers.Add("Transer-Encoding", "chunked");

                var streamContent = new StreamContent(File.OpenRead(filePath));
                //var streamContent = new ByteArrayContent(filecontent);

                request.Content = streamContent;

                HttpResponseMessage response = await client.SendAsync(request);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                throw;
            }
        }
    }
    public byte[] returnByteArrayForCurrentRecording(AudioClip audioClip)
    {
        var samples = new float[audioClip.samples];

        audioClip.GetData(samples, 0);

        Int16[] intData = new Int16[samples.Length];

        Byte[] bytesData = new Byte[samples.Length * 2];

        int rescaleFactor = 32767;

        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * rescaleFactor);
            Byte[] byteArr = new Byte[2];
            byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        return bytesData;
    }

    public static string transcriptId = "";
    async Task ProcessUploadedAudio(string UploadLink)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("Authorization", APITOKEN);

            var json = new
            {
                audio_url = UploadLink
            };

            StringContent payload = new StringContent(JsonConvert.SerializeObject(json), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync("https://api.assemblyai.com/v2/transcript", payload);
            response.EnsureSuccessStatusCode();

            //After we get response
            var responseJson = await response.Content.ReadAsStringAsync();
            dynamic jsondata = JsonConvert.DeserializeObject(responseJson);

            transcriptId = jsondata.id;

            //Update UI
            statusMessageField.text = "Uploading Complete, ID: "+ transcriptId;

            return;
        }
    }
    void OnTranslateAudioBtnClick()
    {
        statusMessageField.text = "Checking Status of: " + transcriptId;

        DownloadTranscript();
    }
    async Task DownloadTranscript()
    {
        //First do http request to ask for link as json
        using (HttpClient httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("Authorization", APITOKEN);
            httpClient.DefaultRequestHeaders.Add("Accepts", "application/json");

            HttpResponseMessage response = await httpClient.GetAsync("https://api.assemblyai.com/v2/transcript/"+ transcriptId);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();

            dynamic jsondata = JsonConvert.DeserializeObject(responseJson);

            //Checkstatus
            string status = jsondata.status;
            if (status == "error") status = jsondata.error;

            statusMessageField.text = "Status: " + status;

            if (status == "completed")
            {
                statusMessageField.text = "Transcript audio: " + jsondata.text;
            }
        }
    }


    //https://answers.unity.com/questions/544264/record-dynamic-length-from-microphone.html
    void EndRecording(AudioSource audS, string deviceName)
    {
        //Capture the current clip data
        AudioClip recordedClip = audS.clip;
        var position = Microphone.GetPosition(deviceName);
        var soundData = new float[recordedClip.samples * recordedClip.channels];
        recordedClip.GetData(soundData, 0);

        //Create shortened array for the data that was used for recording
        var newData = new float[position * recordedClip.channels];

        //$$anonymous$$icrophone.End (null);
        //Copy the used samples to a new array
        for (int i = 0; i < newData.Length; i++)
        {
            newData[i] = soundData[i];
        }

        //One does not simply shorten an AudioClip,
        //    so we make a new one with the appropriate length
        var newClip = AudioClip.Create(recordedClip.name, position, recordedClip.channels, recordedClip.frequency, false);
        newClip.SetData(newData, 0);        //Give it the data from the old clip

        //Replace the old clip
        AudioClip.Destroy(recordedClip);
        audS.clip = newClip;
    }
}
