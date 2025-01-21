using Newtonsoft.Json;
using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
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
            dataGridView.Columns[nameof(LogRecord.Timestamp)].DefaultCellStyle.Format = @"hh\:mm\:ss";
        }
        IList Records { get; } = new SortableBindingList<LogRecord>();

        /// <summary>
        /// An example of a method that conjoins two lists
        /// into one on a separate thread via Task.Run()"
        /// </summary>     
        private async Task RetrieveLogsFromClientMachineAsync(object? sender, EventArgs e)
        {
            IEnumerable? logs = null;
            await Task.Run(async () =>
            {
                // Retrieve the "two lists".
                var syslogTask = _clientMachine.SystemQueryAsync(LogType.SYSLOG);
                var yumStatusTask = _clientMachine.SystemQueryAsync(LogType.VERSION_CHECK);
                await Task.WhenAll(syslogTask, yumStatusTask);
                var syslogResponse = syslogTask.Result;
                var yumResponse = yumStatusTask.Result;
                if (syslogResponse != null && yumResponse != null)
                {
                    var syslogRecords = await localParseResponseAsync(syslogResponse);
                    var yumRecords = await localParseResponseAsync(yumResponse);
                    // Conjoin while still in the background thread.
                    Debug.Assert(InvokeRequired, "Expecting we ARE NOT on the UI thread.");
                    logs = syslogRecords.Concat(yumRecords).OrderBy(record => record.Timestamp);
                }
            });
            // After awaiting the tasks that retrieve the data and combine
            // them, we're back in the UI synchronization context.
            if (logs != null)
            {
                Debug.Assert(!InvokeRequired, "Expecting we ARE on the UI thread.");
                Records.Clear();
                foreach (var record in logs)
                {
                    Records.Add(record);
                }
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
        public async Task<HttpResponseMessage?> SystemQueryAsync(LogType logType)
        {
            Debug.Assert(
                !(SynchronizationContext.Current is WindowsFormsSynchronizationContext),
                 "Expecting we ARE ALREADY NOT on the UI thread.");
            // Even so, it's likely to be async on the server.
            return await Task.Run(() =>
            {
                var names = logType switch
                {
                    LogType.SYSLOG => new[] { "Server started", "Connection lost", "Recovered", "Warning issued", "All systems operational" },
                    LogType.VERSION_CHECK => new[] { "Version check passed", "Update required", "Critical update available", "Version up-to-date", "Unknown version error" },
                    _ => throw new ArgumentOutOfRangeException(nameof(logType), $"Unsupported log type: {logType}")
                };
                var records = names.Select(_ => new LogRecord
                {
                    Type = logType,
                    Description = _,
                    Timestamp = DateTime.UtcNow.AddSeconds(-_rando.NextDouble() * 3600)
                });

                var jsonContent = new StringContent(JsonConvert.SerializeObject(records), Encoding.UTF8, "application/json");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = jsonContent };
            });
        }
    }
}
