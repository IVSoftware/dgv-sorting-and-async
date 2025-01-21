using Newtonsoft.Json;
using System;
using System.Collections;
using System.ComponentModel;

namespace dgv_sorting_and_async
{
    public partial class MainForm : Form
    {
        private readonly ClientMachine _clientMachine = new ClientMachine();
        public MainForm()
        {
            InitializeComponent();
            // Test the 
            buttonUpdate.Click += (sender, e) => 
                _ = RetrieveLogsFromClientMachine(sender, e);
        }

        private async Task RetrieveLogsFromClientMachine(object? sender, EventArgs e)
        {
            var syslogTask = _clientMachine.QuerySYSLOG();
            var yumStatusTask = _clientMachine.QueryYumStatus();
            await Task.WhenAll(syslogTask, yumStatusTask);
            var syslogResponse = await syslogTask;
            var yumResponse = await yumStatusTask;
            if (syslogResponse != null && yumResponse != null)
            {
                var syslogRecords = await ParseResponseAsync(syslogResponse);
                var yumRecords = await ParseResponseAsync(yumResponse);
                // Initial sort by timestamp
                Records.Clear();
                foreach(var record in 
                    syslogRecords.Concat(yumRecords)
                    .OrderBy(record => record.Timestamp))
                {
                    Records.Add(record);
                }
            }
            else
            {
                MessageBox.Show("One or both queries failed.", "Update Failed");
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            dataGridView.DataSource = Records;
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            dataGridView.Columns[nameof(LogRecord.Description)].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        IList Records { get; } = new SortableBindingList<LogRecord>();
        private async Task<List<LogRecord>> ParseResponseAsync(HttpResponseMessage response)
        {
            if( response.IsSuccessStatusCode && 
                JsonConvert
                .DeserializeObject<List<LogRecord>>(await response.Content.ReadAsStringAsync()) is { } records)
            {
                return records;
            }
            else return  new List<LogRecord>();
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
            else
            {
                _isSorted = false;
            }
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
            await Task.Delay(TimeSpan.FromSeconds(_rando.NextDouble()));    // Simulated query time
            var records = new[]
            { "Server started", "Connection lost", "Recovered", "Warning issued", "All systems operational" }
            .Select(_ => new LogRecord
            {
                Type = LogType.SYSLOG,
                Description = _,
                Timestamp = DateTime.UtcNow.AddMinutes(-_rando.Next(5, 60)),
            });

            var jsonContent = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(records), System.Text.Encoding.UTF8, "application/json");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = jsonContent };

        }
        public async Task<HttpResponseMessage?> QueryYumStatus()
        {
            await Task.Delay(TimeSpan.FromSeconds(_rando.NextDouble()));
            var records = new[]
            { "Version check passed", "Update required", "Critical update available", "Version up-to-date", "Unknown version error" }
            .Select(_ => new LogRecord
            {
                Type = LogType.VERSION_CHECK,
                Description = _,
                Timestamp = DateTime.UtcNow.AddMinutes(-_rando.Next(5, 60)),
            });

            var jsonContent = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(records), System.Text.Encoding.UTF8, "application/json");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = jsonContent };

        }
    }
}
