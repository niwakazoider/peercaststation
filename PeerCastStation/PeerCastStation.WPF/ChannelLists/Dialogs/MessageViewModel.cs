// PeerCastStation, a P2P streaming servent.
// Copyright (C) 2013 pethitto (pethitto@gmail.com)
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
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using PeerCastStation.Core;
using PeerCastStation.WPF.ChannelLists.ChannelInfos;
using PeerCastStation.WPF.Commons;
using PeerCastStation.WPF.CoreSettings;

namespace PeerCastStation.WPF.ChannelLists.Dialogs
{
  class MessageViewModel : ViewModelBase
  {

    private Guid channelID;
    public Guid ChannelID
    {
      get { return channelID; }
      set { if (channelID == Guid.Empty) channelID = value; }
    }

    private string channelName = "test";
    public string ChannelName
    {
      get {
        if (channelName.Length > 25) {
          return channelName.Substring(0, 20) + "...";
        }
        else {
          return channelName;
        }
      }
      set { SetProperty("ChannelName", ref channelName, value); }
    }

    private string messageText;
    public string MessageText
    {
      get { return messageText; }
      set { SetProperty("MessageText", ref messageText, value); }
    }

    private readonly Command postMessage;
    public ICommand PostMessage { get { return postMessage; } }

    private PeerCast peerCast;
    public MessageViewModel(PeerCast peerCast)
    {
      this.peerCast = peerCast;
      postMessage = new Command(OnPostMessage, () => CanPost());
    }

    private void OnPostMessage()
    {
      var channel = GetChannel();
      if (channel != null) {
        channel.Post(messageText);
        if (channel.IsBroadcasting) {
          channel.OnMessage(messageText);
        }
      }
    }

    private Channel GetChannel()
    {
      return peerCast.Channels.FirstOrDefault(c => c.ChannelID == channelID);
    }

    private bool CanPost()
    {
      var channel = GetChannel();
      if (channel != null) {
        return true;
      }
      else {
        return false;
      }
    }
  }
}
