Your code and description provide a general idea, and after carefully reviewing it, I believe this can be framed as a [Separation of Concerns (SoC)](https://en.wikipedia.org/wiki/Separation_of_concerns#:~:text=In%20computer%20science%2C%20separation%20of,code%20of%20a%20computer%20program.) issue since the overall objective involves retrieving and merging "two lists" from a remote machine as a background task, while separately managing a `DataGridView` bound to a "sortable binding list" that tracks any changes on the UI thread. The goal, of course, is to keep data retrieval separate from UI logic and ensure that the UI remains responsive.


The main points of this answer are:
 - Bind the data source one time only.
 - Stage the new recordset using Task.Run() and await its completion.
 - Clear and repopulate the binding list with the new data.

I'd like to try and break this all down, but I'm going to have to contrive an example to show what I mean. _I realize this is not "exactly" what you're doing._ We're talking "conceptually" here.

___

**Minimal Example**

>I have a DataGridView that is bound to a custom SortableBindingList that implements sorting [...]

This is a good start, but first let's bind it and be done with it. There's no need to reassign it each time new data arrives.

~~~
public partial class MainForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        dataGridView.DataSource = Records;
        dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        dataGridView.Columns[nameof(LogRecord.Description)].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        dataGridView.Columns[nameof(LogRecord.Timestamp)].DefaultCellStyle.Format = @"hh\:mm\:ss";
    }
    IList Records { get; } = new SortableBindingList<LogRecord>();
    .
    .
    .
}
public enum LogType{ SYSLOG, VERSION_CHECK }
public class LogRecord
{
    public DateTime Timestamp { get; set; }
    public LogType Type { get; set; }
    public string? Description { get; set; }
}
~~~
___

**Custom `SortableBindingList` Implementation Example**
~~~
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
~~~
___

**Client Machine Requirement**

>I have a set of async methods that are responsible for querying and processing of the data, there are certain operations that can only be done on the client machine.

For demo purposes only, we can simulate a plausible scenario of communicating asynchronously with a client machine with an HTTP client.

~~~
public partial class MainForm : Form
{
    .
    .
    .   
    
    private readonly ClientMachine _clientMachine = new ClientMachine();
    public MainForm()
    {
        InitializeComponent();
        // Test the async method by triggering it with the Update button 
        buttonUpdate.Click += (sender, e) => 
            _ = RetrieveLogsFromClientMachineAsync(sender, e);
    }    

    /// <summary>
    /// An example of a method that "conjoins two lists
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
~~~
___

**Client Machine**

Now all we need is a stand-in for "some client machine" that serves up "certain operations that can only be done on the client machine".

~~~
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
~~~