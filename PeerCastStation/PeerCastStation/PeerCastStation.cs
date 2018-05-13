﻿using System;
using PeerCastStation.App;

namespace PeerCastStation.Main
{
  public class PeerCastStation
  {
    [STAThread]
    static int Main(string[] args)
    {
      var basepath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
      var result = StandaloneApp.Run(basepath, args);
      switch (result) {
      case -1:
        return 0;
      default:
        return result;
      }
    }

  }
}
