// PeerCastStation, a P2P streaming servent.
// Copyright (C) 2014 @niwakazoider
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.using System;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PeerCastStation.Core;

namespace PeerCastStation.TS.HLS
{
  public class HLSSourceStreamFactory
    : SourceStreamFactoryBase
  {
    private static readonly Logger logger = new Logger(typeof(HLSSourceStreamFactory));

    public HLSSourceStreamFactory(PeerCast peercast)
      : base(peercast)
    {
    }

    public override string Name
    {
      get { return "HLS Source"; }
    }

    public override string Scheme
    {
      get { return "http"; }
    }

    public override SourceStreamType Type
    {
      get { return SourceStreamType.Broadcast; }
    }

    public override Uri DefaultUri
    {
      get { return new Uri("http://localhost/live/livestream.m3u8"); }
    }

    public override ISourceStream Create(Channel channel, Uri source, IContentReader reader)
    {
      return new HLSSourceStream(PeerCast, channel, source);
    }

  }

  internal class HLSSourceChannel
  {
    private static readonly Logger logger = new Logger(typeof(HLSSourceChannel));

    public Channel TargetChannel { get; private set; }
    public long Position { get { return position; } }

    private float maxRate = 0;
    private long position = 0;
    private int streamIndex = -1;
    private DateTime streamOrigin;
    private RateLimitter limitter = new RateLimitter();
    private MemoryStream bodyBuffer = new MemoryStream();
    private RateCounter recvBytesCounter = new RateCounter(1000);
    public float RecvRate { get { return recvBytesCounter.Rate; } }
    public float SendRate { get { return 0; } }

    public HLSSourceChannel(Channel target_channel)
    {
      this.TargetChannel = target_channel;
    }

    class RateLimitter
    {
      private int byteCount = 0;
      DateTime time = DateTime.Now;
      int sleepTime = 80;

      public void limit(float byteAvaragePerSec, int readByte)
      {
        byteCount += readByte;
        if (byteCount * (1000 / sleepTime) > byteAvaragePerSec)
        {
          var diff = DateTime.Now - time;
          if (diff.TotalMilliseconds < sleepTime)
          {
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(sleepTime).Subtract(diff));
          }
          time = DateTime.Now;
          byteCount = 0;
        }
      }
    }

    public void OnSegment(TsSegmentTask seg)
    {
      if (streamIndex < 0) AddHeader();

      byte[] b = new byte[188];
      float byteAvarage = seg.data.Length / seg.duration;

      for (int i = 0; i < seg.data.Length; i+=188)
      {
        Array.Copy(seg.data, i, b, 0, 188);
        //var packet = new TSPacket(b);
        //if (packet.payload_unit_start_indicator > 0)
        //{
          if (bodyBuffer.Length >= 7144)
          {
            FlushContents();
            updateMaxRate(RecvRate);
          }
        //}
        AddBuffer(b);
        recvBytesCounter.Add(b.Length);

        limitter.limit(byteAvarage, b.Length);
      }

      FlushContents();

    }

    private void updateMaxRate(float i)
    {
      if (i > maxRate * 1.2)
      {
        maxRate = i;

        var info = new AtomCollection(TargetChannel.ChannelInfo.Extra);
        info.SetChanInfoType("TS");
        info.SetChanInfoStreamType("video/mpeg");
        info.SetChanInfoStreamExt(".ts");
        info.SetChanInfoBitrate((int)(RecvRate + SendRate) * 8 / 1000);
        this.TargetChannel.ChannelInfo = new ChannelInfo(info);
      }
    }
    
    private void AddHeader()
    {
      streamIndex = TargetChannel.GenerateStreamID();
      streamOrigin = DateTime.Now;
      position = 0;
      this.TargetChannel.ContentHeader = new Content(streamIndex, TimeSpan.Zero, position, new byte[] { });

      var info = new AtomCollection(TargetChannel.ChannelInfo.Extra);
      info.SetChanInfoType("TS");
      info.SetChanInfoStreamType("video/mpeg");
      info.SetChanInfoStreamExt(".ts");
      this.TargetChannel.ChannelInfo = new ChannelInfo(info);
    }

    private void AddBuffer(byte[] data)
    {
      if (bodyBuffer.Length + data.Length < 8 * 1024 * 1024)
      {
        bodyBuffer.Write(data, 0, data.Length);
      }
    }

    public void FlushContents()
    {
      if (bodyBuffer.Length <= 0) return;
      bodyBuffer.Close();
      byte[] data = bodyBuffer.ToArray();
      this.TargetChannel.Contents.Add(new Content(streamIndex, DateTime.Now - streamOrigin, position, data));
      position += data.Length;
      bodyBuffer = new MemoryStream();
    }

  }

  class TsSegmentTask
  {
    public string url { get; private set; }
    public float duration { get; private set; }
    public byte[] data;
    public TsSegmentTask(string url, float duration)
    {
      this.url = url;
      this.duration = duration;
    }
  }

  public class HLSSourceConnection
    : SourceConnectionBase
  {
    private static readonly Logger logger = new Logger(typeof(HLSSourceConnection));

    public HLSSourceConnection(PeerCast peercast, Channel channel, Uri source_uri)
      : base(peercast, channel, source_uri)
    {
      this.hlsChannel = new HLSSourceChannel(channel);
    }

    private class ConnectionStoppedExcception : ApplicationException { }
    private string playlistUrl = null;
    private float duration = 8.0f;
    private DateTime latest = DateTime.MinValue;
    private TcpClient client;
    private Stream inputstream;
    private Stream outputstream;
    private HLSSourceChannel hlsChannel;
    private IList<TsSegmentTask> tsTaskList = new List<TsSegmentTask>();
    private IList<string> tsGetList = new List<string>();
    private int errorCount = 0;

    public override ConnectionInfo GetConnectionInfo()
    {
      ConnectionStatus status;
      switch (state)
      {
        case ConnectionState.Waiting: status = ConnectionStatus.Connecting; break;
        case ConnectionState.Connected: status = ConnectionStatus.Connecting; break;
        case ConnectionState.Receiving: status = ConnectionStatus.Connected; break;
        case ConnectionState.Error: status = ConnectionStatus.Error; break;
        default: status = ConnectionStatus.Idle; break;
      }
      IPEndPoint endpoint = null;
      if (client != null && client.Connected)
      {
        endpoint = (IPEndPoint)client.Client.RemoteEndPoint;
      }
      return new ConnectionInfo(
        "HLS Source",
        ConnectionType.Source,
        status,
        SourceUri.ToString(),
        endpoint,
        (endpoint != null && Utils.IsSiteLocal(endpoint.Address)) ? RemoteHostStatus.Local : RemoteHostStatus.None,
        hlsChannel.Position,
        hlsChannel.RecvRate,
        hlsChannel.SendRate,
        null,
        null,
        clientName);
    }

    private enum ConnectionState
    {
      Waiting,
      Connected,
      Receiving,
      Error,
      Closed,
    };
    private ConnectionState state = ConnectionState.Waiting;
    private string clientName = "HLS Client 0.1";

    protected override StreamConnection DoConnect(Uri source)
    {
      TcpClient client = null;
      this.inputstream = new MemoryStream();
      this.outputstream = new MemoryStream();
      this.client = client;
      this.errorCount = 0;
      return new StreamConnection(inputstream, outputstream);

    }

    protected override void DoProcess()
    {
      while (!IsStopped)
      {
        RecvUntil(RecvWait, RecvHlsSegment);
      }
    }

    protected override void DoPost(Host from, Atom packet)
    {
      //Do nothing
    }

    protected override void DoClose(StreamConnection connection)
    {
      this.connection.Close();
      if (client != null)
      {
        this.client.Close();
      }
      Logger.Debug("Closed");
    }

    public override void Run()
    {
      this.state = ConnectionState.Waiting;
      try
      {
        OnStarted();
        if (connection != null && !IsStopped)
        {
          DoProcess();
        }
        this.state = ConnectionState.Closed;
      }
      catch (IOException e)
      {
        Logger.Error(e);
        DoStop(StopReason.ConnectionError);
        this.state = ConnectionState.Error;
      }
      catch (ConnectionStoppedExcception e)
      {
        this.state = ConnectionState.Closed;
      }
      SyncContext.ProcessAll();
      OnStopped();
    }

    private bool RecvWait()
    {
      System.Threading.Thread.Sleep(500);
      TimeSpan diff = DateTime.Now - latest;
      TimeSpan dur = TimeSpan.FromSeconds(duration);
      if (diff < dur)
      {
        //var time = dur.Subtract(diff);
        //System.Threading.Thread.Sleep(time);
        return true;
      }
      else
      {
        latest = DateTime.Now;
        return false;
      }

    }

    private bool RecvHlsSegment()
    {
      for (int i = 0; i < 3; i++)
      {
        try
        {
          getPlayList();
          if (tsTaskList.Count > 0)
          {
            getPlayData();
            errorCount = 0;
          }
          else
          {
            throw new WebException();
          }
          break;
        }
        catch (WebException ex)
        {
          errorCount++;
          if (errorCount > 2)
          {
            this.state = ConnectionState.Error;
            DoStop(StopReason.ConnectionError);
          }
          Logger.Debug(ex);
          Thread.Sleep(3000);
        }
        catch(Exception e)
        {
          this.state = ConnectionState.Error;
          DoStop(StopReason.ConnectionError);
          Logger.Debug(e);
        }
      }
      return true;
    }

    private string getPlayList()
    {
      string sURL = playlistUrl == null ? this.SourceUri.ToString() : playlistUrl;
      byte[] data = HTTP.Get(sURL);
      string str = Encoding.UTF8.GetString(data);
      var dict = parseM3U8(sURL, str);
      if (dict == null && playlistUrl != null)
      {
        data = HTTP.Get(playlistUrl);
        str = Encoding.UTF8.GetString(data);
        parseM3U8(sURL, str);
      }
      if (this.state != ConnectionState.Receiving)
      {
        this.state = ConnectionState.Connected;
      }
      Logger.Debug("getPlayList:" + playlistUrl);
      //System.Threading.Thread.Sleep(1000);
      return str;
    }

    private byte[] getTsData(string sURL)
    {
      byte[] data = HTTP.Get(sURL);
      Logger.Debug("getTsData:" + sURL);
      //System.Threading.Thread.Sleep(1000);
      return data;
    }

    private void getPlayData()
    {
      foreach (var seg in tsTaskList)
      {
        if (!tsGetList.Contains(seg.url))
        {
          tsGetList.Add(seg.url);
          byte[] data = getTsData(seg.url);
          seg.data = data;
          this.state = ConnectionState.Receiving;
          hlsChannel.OnSegment(seg);
        }
      }
      tsTaskList.Clear();
    }


    private Dictionary<string, object> parseM3U8(string sURL, string str)
    {
      var dict = new Dictionary<string, object>();
      string[] lines = str.Split('\n');
      for (var i=0; i<lines.Length; i++)
      {
        string line = lines[i];
        string[] pair = line.Split(':');
        if (pair.Length < 2) continue;
        string key = pair[0];
        string value = pair[1];
        if (key == "#EXT-X-STREAM-INF")
        {
          i++;
          playlistUrl = lines[i];
          return null;
        }
        if (key == "#EXTINF")
        {
          duration = float.Parse(value.Split(',')[0]);
          if (duration < 1) duration = 1.0f;
          i++;
          line = GetAbsoluteURL(sURL, lines[i]);
          tsTaskList.Add(new TsSegmentTask(line, duration));
        }
        else
        {
          dict[key] = value;
        }
      }
      if (playlistUrl == null)
      {
        playlistUrl = sURL;
      }
      return dict;
    }

    private string GetAbsoluteURL(string url, string path)
    {
      Uri u = new Uri(url);
      if (path.StartsWith("http:"))
      {
        return path;
      }
      else if (path.StartsWith("/"))
      {
        string auth = u.GetLeftPart(UriPartial.Authority);
        return auth + path;
      }
      else
      {
        string upath = u.GetLeftPart(UriPartial.Path);
        string dir = upath.Substring(0, upath.LastIndexOf("/") + 1);
        return dir + path;
      }
    }

    protected void RecvUntil(Func<bool> wait, Func<bool> proc)
    {
      WaitAndProcessEvents(connection.ReceiveWaitHandle, stopped =>
      {
        if (stopped)
        {
          throw new ConnectionStoppedExcception();
        }
        if (!wait())
        {
          proc();
        }
        return null;
      });
    }

    protected bool WaitAndProcessEvents(WaitHandle wait_handle, Func<bool, WaitHandle> on_signal)
    {
      var handles = new WaitHandle[] {
        SyncContext.EventHandle,
        null,
      };
      bool event_processed = false;
      while (wait_handle != null)
      {
        handles[1] = wait_handle;
        var idx = WaitHandle.WaitAny(handles);
        if (idx == 0)
        {
          SyncContext.ProcessAll();
          if (IsStopped)
          {
            wait_handle = on_signal(IsStopped);
          }
          event_processed = true;
        }
        else
        {
          wait_handle = on_signal(IsStopped);
        }
      }
      if (!event_processed)
      {
        SyncContext.ProcessAll();
      }
      return true;
    }

  }

  public class HLSSourceStream
  : SourceStreamBase
  {
    private static readonly Logger logger = new Logger(typeof(HLSSourceStream));

    public HLSSourceStream(PeerCast peercast, Channel channel, Uri source_uri)
      : base(peercast, channel, source_uri)
    {
    }

    public override SourceStreamType Type
    {
      get { return SourceStreamType.Broadcast; }
    }

    public override ConnectionInfo GetConnectionInfo()
    {
      if (sourceConnection != null)
      {
        return sourceConnection.GetConnectionInfo();
      }
      else
      {
        ConnectionStatus status;
        switch (StoppedReason)
        {
          case StopReason.UserReconnect: status = ConnectionStatus.Connecting; break;
          case StopReason.UserShutdown: status = ConnectionStatus.Idle; break;
          default: status = ConnectionStatus.Error; break;
        }
        IPEndPoint endpoint = null;
        string client_name = "";
        return new ConnectionInfo(
          "HLS Source",
          ConnectionType.Source,
          status,
          SourceUri.ToString(),
          endpoint,
          RemoteHostStatus.None,
          null,
          null,
          null,
          null,
          null,
          client_name);
      }
    }

    protected override ISourceConnection CreateConnection(Uri source_uri)
    {
      return new HLSSourceConnection(PeerCast, Channel, source_uri);
    }

    protected override void OnConnectionStopped(SourceStreamBase.ConnectionStoppedEvent msg)
    {
      switch (msg.StopReason)
      {
        case StopReason.UserReconnect:
          break;
        case StopReason.UserShutdown:
          Stop(msg.StopReason);
          break;
        default:
          Reconnect();
          break;
      }
    }

  }

  [Plugin]
  class HLSSourceStreamPlugin
    : PluginBase
  {
    override public string Name { get { return "HLS Source"; } }

    private HLSSourceStreamFactory factory;
    override protected void OnAttach()
    {
      if (factory == null) factory = new HLSSourceStreamFactory(Application.PeerCast);
      Application.PeerCast.SourceStreamFactories.Add(factory);
    }

    override protected void OnDetach()
    {
      Application.PeerCast.SourceStreamFactories.Remove(factory);
    }
  }
}
