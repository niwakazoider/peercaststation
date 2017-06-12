using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net;
using System.Collections.Generic;
using WebSocketSharp;

namespace PeerCastStation.Core
{
  public class NatTraversal
  {
    private Thread wsthread;
    private Thread sdthread;
    private int retryCount = 0;
    private WebSocket _ws = null;
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

    private void WsConnect()
    {
        try {
          Thread.Sleep(5000);

          if (_ws == null) {
              _ws = new WebSocket(wsshost);
              _ws.OnOpen += (sender, e) => {
                retryCount = 0;
                logger.Info("Connected to Nat Traversal matching server.");
              };
              _ws.OnError += (sender, e) => {
                retryCount++;
              };
              _ws.OnClose += (sender, e) => {
                retryCount++;
                logger.Info("Nat Traversal matching server error.");
                _ws = null;
                
                if(retryCount<10) {
                  WsConnect();
                }
              };
              _ws.OnMessage += (sender, e) => {
                  SimultaneousOpen(e.Data);
              };
          }
          _ws.Connect();

        } catch(Exception e) {
          Debug.WriteLine(e);
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
        if (_ws.ReadyState == WebSocketState.Open) {
            var remoteIP = text.Split(':')[0];
        
            if(!clients.ContainsKey(remoteIP)) {
              clients[remoteIP] = null;
            }
            
            _ws.Send(Encoding.UTF8.GetBytes(text));

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
      } catch(ThreadAbortException) {
      } catch(Exception) { }
      try {
        sdthread.Abort();
      } catch(ThreadAbortException) {
      } catch(Exception) { }
      try {
        _ws.Close();
      } catch(Exception) { }
    }

    private static Logger logger = new Logger(typeof(NatTraversal));

  }
}
