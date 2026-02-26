const fs = require('fs');
fetch('http://localhost:8000/rest/v1/rpc/send_invite', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'apikey': 'okie_rummy_anon_key_2025',
    'Authorization': 'Bearer okie_rummy_anon_key_2025' // Wait, the RPC in the Godot client uses the real session access token!
  }
});
