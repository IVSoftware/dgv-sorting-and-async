using Newtonsoft.Json;
using System;
using System.Collections;
using System.ComponentModel;
using System.Net;
using System.Text;

namespace dgv_sorting_and_async
{
    public partial class MainForm : Form
    {
        private readonly ClientMachine _clientMachine = new ClientMachine();
        public MainForm()
        {
            InitializeComponent();
            // Test the async method by triggering it with the Update button 
            buttonUpdate.Click += (sender, e) => 
                _ = RetrieveLogsFromClientMachineAsync(sender, e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            dataGridView.DataSource = Records;
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            dataGridView.Columns[nameof(LogRecord.Description)].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        IList Records { get; } = new SortableBindingList<LogRecord>();
        
        private async Task RetrieveLogsFromClientMachineAsync(object? sender, EventArgs e)
        {
            // "The [...] method that is fired [...] conjoins two
            // lists into one on a separate thread via Task.Run()"
            await Task.Run(async() =>
            {
                var syslogTask = _clientMachine.QuerySYSLOG();
                var yumStatusTask =  _clientMachine.QueryYumStatus();
                await Task.WhenAll(syslogTask, yumStatusTask);
                var syslogResponse = syslogTask.Result;
                var yumResponse = yumStatusTask.Result;
                if (syslogResponse != null && yumResponse != null)
                {
                    var syslogRecords = await localParseResponseAsync(syslogResponse);
                    var yumRecords = await localParseResponseAsync(yumResponse);
                    // Marshal back onto the UI thread.
                    BeginInvoke(() =>
                    { 
                        // Initial sort by timestamp
                        Records.Clear();
                        foreach(var record in 
                            syslogRecords.Concat(yumRecords)
                            .OrderBy(record => record.Timestamp))
                        {
                            Records.Add(record);
                        }
                    });
                }
                else
                {
                    MessageBox.Show("One or both queries failed.", "Update Failed");
                }
                async Task<List<LogRecord>> localParseResponseAsync(HttpResponseMessage response)
                {
                    if (response.IsSuccessStatusCode &&
                        JsonConvert
                        .DeserializeObject<List<LogRecord>>(await response.Content.ReadAsStringAsync()) is { } records)
                    {
                        return records;
                    }
                    else return new List<LogRecord>();
                }
            });
        }
    }
    public enum LogType{ SYSLOG, VERSION_CHECK }
    public class LogRecord
    {
        public DateTime Timestamp { get; set; }
        public LogType Type { get; set; }
        public string? Description { get; set; }
    }
    public class SortableBindingList<T> : BindingList<T>
    {
        protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction)
        {
            if (prop != null)
            {
                var items = this.Items as List<T>;
                if (items != null)
                {
                    var propValue = prop.GetValue;
                    if (direction == ListSortDirection.Ascending)
                    {
                        items.Sort((x, y) => Comparer<object>.Default.Compare(propValue(x), propValue(y)));
                    }
                    else
                    {
                        items.Sort((x, y) => Comparer<object>.Default.Compare(propValue(y), propValue(x)));
                    }
                    _sortProperty = prop;
                    _sortDirection = direction;
                    _isSorted = true;
                }
            }
            else _isSorted = false;
            ResetBindings();
        }
        private bool _isSorted;
        private PropertyDescriptor? _sortProperty;
        private ListSortDirection _sortDirection;
        protected override bool SupportsSortingCore => true;
        protected override bool IsSortedCore => _isSorted;
        protected override ListSortDirection SortDirectionCore => _sortDirection;
        protected override PropertyDescriptor? SortPropertyCore => _sortProperty;
        protected override void RemoveSortCore() => _isSorted = false;
    }
    class ClientMachine
    {
        Random _rando = new Random(1);
        public async Task<HttpResponseMessage?> QuerySYSLOG()
        {
            return await Task.Run(() =>
            {
                var records = new[]
                { "Server started", "Connection lost", "Recovered", "Warning issued", "All systems operational" }
                .Select(_ => new LogRecord
                {
                    Type = LogType.SYSLOG,
                    Description = _,
                    Timestamp = DateTime.UtcNow.AddMinutes(-_rando.Next(5, 60)),
                });

                var jsonContent = new StringContent(JsonConvert.SerializeObject(records), Encoding.UTF8, "application/json");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = jsonContent };
            });
        }
        public async Task<HttpResponseMessage?> QueryYumStatus()
        {
            return await Task.Run(() =>
            {
                var records = new[]
                { "Version check passed", "Update required", "Critical update available", "Version up-to-date", "Unknown version error" }
                .Select(_ => new LogRecord
                {
                    Type = LogType.VERSION_CHECK,
                    Description = _,
                    Timestamp = DateTime.UtcNow.AddMinutes(-_rando.Next(5, 60)),
                });
                var jsonContent = new StringContent(JsonConvert.SerializeObject(records), Encoding.UTF8, "application/json");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = jsonContent };
            });
        }
    }
}
