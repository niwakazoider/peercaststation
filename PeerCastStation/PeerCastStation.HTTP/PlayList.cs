﻿// PeerCastStation, a P2P streaming servent.
// Copyright (C) 2011 Ryuichi Sakamoto (kumaryu@kumaryu.net)
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
using PeerCastStation.Core;

namespace PeerCastStation.HTTP
{
  /// <summary>
  /// プレイリストを表すインターフェースです
  /// </summary>
  public interface IPlayList
  {
    /// <summary>
    /// プレイリスト自体のContent-Typeを取得します
    /// </summary>
    string MIMEType { get; }

    /// <summary>
    /// プレイリストに含まれるチャンネルのコレクションを取得します
    /// </summary>
    IList<Channel> Channels { get; }

    /// <summary>
    /// Channelsを参照してプレイリストを作成し文字列で返します
    /// </summary>
    /// <param name="baseuri">ベースとなるURI</param>
    /// <returns>作成したプレイリスト</returns>
    byte[] CreatePlayList(Uri baseuri);
  }

  /// <summary>
  /// URLを列挙するだけの簡単なプレイリストを作成するクラスです
  /// </summary>
  public class M3UPlayList
    : IPlayList
  {
    public string MIMEType { get { return "audio/x-mpegurl"; } }
    public IList<Channel> Channels { get; private set; }

    public M3UPlayList()
    {
      Channels = new List<Channel>();
    }

    public byte[] CreatePlayList(Uri baseuri)
    {
      var res = new System.Text.StringBuilder();
      foreach (var c in Channels) {
        var url = new Uri(baseuri, c.ChannelID.ToString("N").ToUpper() + c.ChannelInfo.ContentExtension);
        res.AppendLine(url.ToString());
      }
      return System.Text.Encoding.UTF8.GetBytes(res.ToString());
    }
  }

  /// <summary>
  /// ASX形式のプレイリストを作成するクラスです
  /// </summary>
  public class ASXPlayList
    : IPlayList
  {
    public string MIMEType { get { return "video/x-ms-asf"; } }
    public IList<Channel> Channels { get; private set; }

    public ASXPlayList()
    {
      Channels = new List<Channel>();
    }

    public byte[] CreatePlayList(Uri baseuri)
    {
      var stream = new System.IO.StringWriter();
      var xml = new System.Xml.XmlTextWriter(stream);
      xml.Formatting = System.Xml.Formatting.Indented;
      xml.WriteStartElement("ASX");
      xml.WriteAttributeString("version", "3.0");
      if (Channels.Count>0) {
        xml.WriteElementString("Title", Channels[0].ChannelInfo.Name);
      }
      foreach (var c in Channels) {
        string name = c.ChannelInfo.Name;
        string contact_url = null;
        if (c.ChannelInfo.URL!=null) {
          contact_url = c.ChannelInfo.URL;
        }
        var stream_url = new UriBuilder(baseuri);
        stream_url.Scheme = "mms";
        if (stream_url.Path[stream_url.Path.Length-1]!='/') {
          stream_url.Path += '/';
        }
        stream_url.Path +=
          c.ChannelID.ToString("N").ToUpper() +
          c.ChannelInfo.ContentExtension;
        xml.WriteStartElement("Entry");
        xml.WriteElementString("Title", name);
        if (contact_url!=null && contact_url!="") {
          xml.WriteStartElement("MoreInfo");
          xml.WriteAttributeString("href", contact_url);
          xml.WriteEndElement();
        }
        xml.WriteStartElement("Ref");
        xml.WriteAttributeString("href", stream_url.Uri.ToString());
        xml.WriteEndElement();
        xml.WriteEndElement();
      }
      xml.WriteEndElement();
      xml.Close();
      var res = stream.ToString();
      try {
        return System.Text.Encoding.GetEncoding(932).GetBytes(res);
      }
      catch (System.Text.EncoderFallbackException) {
        return System.Text.Encoding.UTF8.GetBytes(res);
      }
    }
  }
}
