import jwt
import datetime

def generate_token(role, secret):
    payload = {
        "role": role,
        "iss": "supabase",
        "iat": int(datetime.datetime.now().timestamp()),
        "exp": int((datetime.datetime.now() + datetime.timedelta(days=3650)).timestamp())
    }
    return jwt.encode(payload, secret, algorithm="HS256")

secret = "okey-rummy-jwt-secret-that-is-at-least-32-chars-long-2025"
print(f"ANON_KEY={generate_token('anon', secret)}")
print(f"SERVICE_ROLE_KEY={generate_token('service_role', secret)}")
