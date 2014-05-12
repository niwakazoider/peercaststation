using System;
using System.Collections.Generic;
using System.Linq;
using PeerCastStation.Core;

namespace PeerCastStation
{
  public class MessageNotifier
    : IChannelMonitor
  {
    private static TimeSpan messageExpires = TimeSpan.FromMinutes(1);
    public static TimeSpan MessageExpires
    {
      get { return messageExpires; }
      set { messageExpires = value; }
    }
    private System.Diagnostics.Stopwatch messageExpireTimer = new System.Diagnostics.Stopwatch();
    private NotificationMessage lastMessage;
    private PeerCastApplication app;
    public MessageNotifier(PeerCastApplication app)
    {
      this.app = app;
      this.messageExpireTimer.Start();
      this.app.PeerCast.ChannelAdded += (sender, args) =>
      {
        args.Channel.MessageReceived += OnChannelMessagePosted;
      };
      this.app.PeerCast.ChannelRemoved += (sender, args) =>
      {
        args.Channel.MessageReceived -= OnChannelMessagePosted;
      };
    }

    public void OnChannelMessagePosted(object sender, EventArgs args)
    {
      var channel = (Channel)sender;
      var msg = new NotificationMessage(
        channel.ChannelInfo.Name,
        channel.Messages[channel.Messages.Count - 1],
        NotificationMessageType.Info);
      NotifyMessage(msg);
    }

    private void NotifyMessage(NotificationMessage msg)
    {
      lock (messageExpireTimer)
      {
        if (messageExpireTimer.Elapsed >= MessageExpires)
        {
          lastMessage = null;
          messageExpireTimer.Reset();
          messageExpireTimer.Start();
        }
        if (lastMessage == null || !lastMessage.Equals(msg))
        {
          foreach (var ui in this.app.Plugins.Where(p => p is IUserInterfacePlugin))
          {
            ((IUserInterfacePlugin)ui).ShowNotificationMessage(msg);
          }
          lastMessage = msg;
        }
      }
    }

    public void OnTimer()
    {
    }
  }
}
