
var MessagesViewModel = new function() {
  var self = this;
  self.messages = ko.observable('');

  self.updating = false;
  self.update = function() {
    PeerCast.getMessage(null, null, function(result) {
      if (result && result.lines>0) {
        self.updating = true;
        self.messages(result.message);
        self.updating = false;
      }
    });
  };

  self.clear = function() {
    PeerCast.clearMessage(function() {
      self.messages('');
    });
  };

  self.bind = function(target) {
    self.update();
    ko.applyBindings(self, target);
    setInterval(self.update, 1000);
  };
};

