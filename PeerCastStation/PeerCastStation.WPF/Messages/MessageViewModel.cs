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
using System.IO;
using System.Windows.Input;
using PeerCastStation.Core;
using PeerCastStation.WPF.Commons;
using System.Collections.Generic;

namespace PeerCastStation.WPF.Logs
{
  class MessageViewModel : ViewModelBase
  {
    private readonly LogWriter logWriter = new LogWriter(1000);

    public string Message { get { return logWriter.ToString(); } }

    private readonly ICommand clear;
    public ICommand Clear { get { return clear; } }

    PeerCast peerCast;
    internal MessageViewModel(PeerCast peerCast)
    {
      this.peerCast = peerCast;

      clear = new Command(() =>
        {
          logWriter.Clear();
          OnPropertyChanged("Message");
        });

      peerCast.ChannelAdded += (sender, args) =>
      {
        args.Channel.MessageReceived += OnChannelMessagePosted;
      };
      peerCast.ChannelRemoved += (sender, args) =>
      {
        args.Channel.MessageReceived -= OnChannelMessagePosted;
      };
    }

    public void OnChannelMessagePosted(object sender, EventArgs args)
    {
      var channel = (Channel)sender;
      var msg = channel.Messages[channel.Messages.Count - 1];
      var name = channel.ChannelInfo.Name;
      DateTime dt = DateTime.Now;
      var time = dt.Hour + ":" + dt.Minute;
      logWriter.WriteLine(name + " (" + time + ")\n  " + msg);
    }

    internal void UpdateLog()
    {
      OnPropertyChanged("Message");
    }
  }
}
