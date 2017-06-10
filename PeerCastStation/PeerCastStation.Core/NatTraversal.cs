using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Net;
using System.Collections.Generic;

namespace PeerCastStation.Core
{
  public class NatTraversal
  {
    private Thread wsthread;
    private Thread sdthread;
    private int MessageBufferSize = 256;
    private int retryCount = 0;
    private ClientWebSocket _ws = null;
    private string wsshost = "ws://127.0.0.1:3000/";
    private Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>();

    public NatTraversal() {
      wsthread = new Thread(WsConnect);
      wsthread.Start();
      sdthread = new Thread(AutoShutdown);
      sdthread.Start();
    }

    private void AutoShutdown() {
      Thread.Sleep(30*60*1000);
      PeerCastApplication.Current.Stop();
    }

    private void SimultaneousOpen(string host) {
      TcpClient client = null;
      string remoteIP = null;
      try {
        var p = host.Split(':');
        remoteIP = p[0];
        int remotePort = int.Parse(p[1]);
        IPEndPoint ipLocalEndPoint = new IPEndPoint(IPAddress.Any, remotePort);
        client = new TcpClient(ipLocalEndPoint);
        //for(int retry=0;retry<1;retry++) {
          try{
            logger.Info("Nat Traversal connect to "+host);
            client.Connect(new IPEndPoint(IPAddress.Parse(remoteIP), remotePort));
            logger.Info("Nat Traversal connected!");
            
            Task.Run( () => {
              try {
                Thread.Sleep(5000);
                clients.Remove(remoteIP);
              }catch(Exception) {}
            } );

            if(!clients.ContainsKey(remoteIP)) {
              clients[remoteIP] = client;
              OnListener(client);
            }
            else {
              clients[remoteIP] = client;
            }
            return;
          }catch(SocketException s) {
            Debug.WriteLine(s.Message);
          }
        //}
      }catch(Exception e) {
        Debug.WriteLine(e.Message);
      }
      try{
        if(client!=null) {
          client.Close();
        }
      }catch(Exception) {
      }
    }

    private async void WsConnect()
    {
        if (_ws == null) {
            _ws = new ClientWebSocket();
        }

        try {

          Thread.Sleep(5000);
 
          if (_ws.State != WebSocketState.Open) {
              await _ws.ConnectAsync(new Uri(wsshost), CancellationToken.None);

              if(_ws.State == WebSocketState.Open) {
                retryCount = 0;
                logger.Info("Connected to Nat Traversal matching server.");
              }
 
              while (_ws.State == WebSocketState.Open) {
                var buff = new ArraySegment<byte>(new byte[MessageBufferSize]);
                var ret = await _ws.ReceiveAsync(buff, CancellationToken.None);
                var remote = (new UTF8Encoding()).GetString(buff.Take(ret.Count).ToArray());
           
                if(remote=="") continue;

                await Task.Run(() =>
                {
                  SimultaneousOpen(remote);
                });

              }
          }

        } catch(Exception e) {
          Debug.WriteLine(e);

          retryCount++;
          logger.Info("Nat Traversal matching server error.");

          try {
            _ws.Dispose();
          } catch(Exception) { }

          if(retryCount>10) {
            return;
          }

          _ws = null;

          WsConnect();
        }

    }

    private void OnListener(TcpClient client) {
      logger.Info("Nat Traversal client connected {0}", client.Client.RemoteEndPoint);
      var listener = PeerCastApplication.Current.PeerCast.NatListener();
            var client_task = listener.ConnectionHandler.HandleClient(
              client,
              listener.GetAccessControlInfo(client.Client.RemoteEndPoint as IPEndPoint));
    }

    public TcpClient Match(string text)
    {
      TcpClient client = null;
      try {
        var buff = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
        if (_ws.State == WebSocketState.Open) {
            var remoteIP = text.Split(':')[0];
        
            if(!clients.ContainsKey(remoteIP)) {
              clients[remoteIP] = null;
            }
            
            _ws.SendAsync(buff, WebSocketMessageType.Text, true, CancellationToken.None);

            for(var i=0;i<30;i++) {
              Thread.Sleep(100);
              if(clients.ContainsKey(remoteIP) && clients[remoteIP] != null) {
                client = clients[remoteIP];
                break;
              }
            }

            if(clients.ContainsKey(remoteIP)) {
              clients.Remove(remoteIP);
            }
        }
      } catch(Exception) { }
      return client;
    }

    public void Stop()
    {
      try {
        wsthread.Abort();
      } catch(Exception) { }
      try {
        sdthread.Abort();
      } catch(Exception) { }
      try {
        _ws.Dispose();
      } catch(Exception) { }
    }

    private static Logger logger = new Logger(typeof(NatTraversal));

  }
}
