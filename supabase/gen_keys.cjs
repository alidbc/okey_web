const crypto = require('crypto');

function base64url(obj) {
    return Buffer.from(JSON.stringify(obj))
        .toString('base64')
        .replace(/=/g, '')
        .replace(/\+/g, '-')
        .replace(/\//g, '_');
}

function sign(role, secret) {
    const header = { alg: "HS256", typ: "JWT" };
    const payload = {
        role: role,
        iss: "supabase",
        iat: Math.floor(Date.now() / 1000),
        exp: Math.floor(Date.now() / 1000) + 60 * 60 * 24 * 365 * 10
    };

    const unsignedToken = base64url(header) + "." + base64url(payload);
    const signature = crypto
        .createHmac('sha256', secret)
        .update(unsignedToken)
        .digest('base64')
        .replace(/=/g, '')
        .replace(/\+/g, '-')
        .replace(/\//g, '_');

    return unsignedToken + "." + signature;
}

const secret = "okey-rummy-jwt-secret-that-is-at-least-32-chars-long-2025";
console.log("ANON_KEY=" + sign("anon", secret));
console.log("SERVICE_ROLE_KEY=" + sign("service_role", secret));
