using System;
using System.Collections;
using System.IO.Ports;
using System.Text;
using UnityEngine;

/// <summary>USB serial connection. Commands are printable ASCII lines, max 255 characters.</summary>
public sealed class Esp32SerialConnection : MonoBehaviour
{
    [Tooltip("Use List Serial Ports in this component's context menu to find the device.")]
    [SerializeField] private string portName = "/dev/cu.usbserial-0001";
    [SerializeField] private int baudRate = 115200;
    [SerializeField] private string testCommand = "HELLO ESP32";
    [SerializeField] private bool sendTestOnConnect = true;
    private SerialPort port;
    private Coroutine startup;
    private bool ready;
    private bool overflow;
    // Complete lines are delivered on Unity's main thread, without the log prefix.
    public event Action<string> LineReceived;
    public event Action Disconnected;
    private readonly StringBuilder received = new StringBuilder();
    public bool IsConnected => ready && port != null && port.IsOpen;

    private void OnEnable() => Connect();

    [ContextMenu("Connect")]
    public void Connect()
    {
        Disconnect();
        try
        {
            port = new SerialPort(portName, baudRate)
            {
                NewLine = "\n", Encoding = Encoding.ASCII,
                ReadTimeout = 10, WriteTimeout = 100,
                DtrEnable = false, RtsEnable = false
            };
            port.Open();
            startup = StartCoroutine(WaitForBoot());
        }
        catch (Exception e)
        {
            Debug.LogError($"ESP32 connection failed on {portName}: {e.Message}", this);
            Disconnect();
        }
    }

    private IEnumerator WaitForBoot()
    {
        // Opening a USB serial port can reset the board.
        yield return new WaitForSecondsRealtime(2f);
        ready = true;
        startup = null;
        Debug.Log($"ESP32 connected: {portName}", this);
        if (sendTestOnConnect) { SendTestCommand(); sendTestOnConnect = false; }
    }

    public void SendCommand(string command)
    {
        TrySendCommand(command);
    }

    /// <summary>Return false if unavailable/invalid instead of making callers assume success.</summary>
    public bool TrySendCommand(string command)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("ESP32 is disconnected or still booting.", this);
            return false;
        }
        if (command == null || command.Length > 255 ||
            Array.Exists(command.ToCharArray(), c => c < 32 || c > 126))
        {
            Debug.LogWarning("Use one line of up to 255 printable ASCII characters.", this);
            return false;
        }
        try { port.WriteLine(command); return true; }
        catch (Exception e) { Debug.LogError($"ESP32 send failed: {e.Message}", this); Disconnect(); return false; }
    }

    [ContextMenu("Send Test Command")]
    public void SendTestCommand() => SendCommand(testCommand);

    private void Update()
    {
        if (port == null || !port.IsOpen) return;
        try
        {
            // Read available bytes only, with bounded work per frame.
            int count = Math.Min(port.BytesToRead, 1024);
            for (int i = 0; i < count; i++)
            {
                int c = port.ReadByte();
                if (c == '\n')
                {
                    if (!overflow)
                    {
                        string line = received.ToString();
                        received.Clear();
                        Debug.Log($"ESP32 echo: {line}", this);
                        // A listener's failure must not close a healthy serial connection.
                        if (LineReceived != null)
                            foreach (Action<string> listener in LineReceived.GetInvocationList())
                                try { listener(line); }
                                catch (Exception e) { Debug.LogException(e, this); }
                        // A listener may intentionally disconnect while handling a line.
                        if (port == null || !port.IsOpen) return;
                    }
                    received.Clear();
                    overflow = false;
                }
                else if (c != '\r' && !overflow)
                {
                    if (received.Length < 1024) received.Append((char)c);
                    else { received.Clear(); overflow = true; }
                }
            }
        }
        catch (TimeoutException) { }
        catch (Exception e) { Debug.LogError($"ESP32 receive failed: {e.Message}", this); Disconnect(); }
    }

    [ContextMenu("List Serial Ports")]
    public void ListSerialPorts()
    {
        try { Debug.Log("Serial ports: " + string.Join(", ", SerialPort.GetPortNames()), this); }
        catch (Exception e) { Debug.LogError(e.Message, this); }
    }

    [ContextMenu("Disconnect")]
    public void Disconnect()
    {
        bool hadConnection = port != null || ready;
        if (startup != null) { StopCoroutine(startup); startup = null; }
        ready = false;
        received.Clear();
        overflow = false;
        try { port?.Dispose(); }
        catch (Exception e) { Debug.LogWarning(e.Message, this); }
        finally { port = null; }
        if (hadConnection && Disconnected != null)
            foreach (Action listener in Disconnected.GetInvocationList())
                try { listener(); }
                catch (Exception e) { Debug.LogException(e, this); }
    }

    private void OnDisable() => Disconnect();
    private void OnApplicationQuit() => Disconnect();
}
