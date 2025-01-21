Your code and description provide a general idea, and after carefully looking at it my view is that it can be framed as a [Separation of Concerns (SoC)](https://en.wikipedia.org/wiki/Separation_of_concerns#:~:text=In%20computer%20science%2C%20separation%20of,code%20of%20a%20computer%20program.) issue. In particular, we can identify the _single point_ where the data processing that is on the background thread needs to be marshaled back onto the UI thread.

I'd like to try and break it down, but I'm going to have to contrive an example to show what I mean. _I realize this is not "exactly" what you're doing._ We're talking "conceptually" here.
___

>I have a DataGridView that is bound to a custom SortableBindingList that implements sorting [...]

This is a good start, but first let's bind it and be done with it.

~~~
public partial class MainForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        dataGridView.DataSource = Records;
        dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        dataGridView.Columns[nameof(LogRecord.Description)].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
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

**Custom Sortable Binding List Example**
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
    /// An example of a method that conjoins two lists
    /// into one on a separate thread via Task.Run()"
    /// </summary>        
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
~~~

**Client Machine**

Now all we need is a stand-in for "some client machine" that serves up "certain operations that can only be done on the client machine".

~~~
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
~~~