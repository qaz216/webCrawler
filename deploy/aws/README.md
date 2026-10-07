# Deploying the demo to AWS (single EC2 instance)

One small EC2 instance runs the whole stack with Docker Compose. Caddy in front provides automatic
HTTPS and a login prompt; nothing else is reachable from the internet.

```
Internet ──443/80──▶ Caddy (HTTPS + login) ──▶ web (nginx: UI, /api proxy) ──▶ api
                                                                   worker ◀──▶ RabbitMQ, PostgreSQL
                     (only Caddy publishes ports; everything else is on Docker's internal network)
```

| | |
|---|---|
| Instance | `t3.small` (2 vCPU, 2 GB RAM + 2 GB swap), Ubuntu 24.04, 20 GB disk |
| Cost | ≈ $0.025/hour running (≈ $20/month 24/7); stopped: only the disk, ≈ $1.60/month |
| Address | `https://<public-ip-with-dashes>.sslip.io` — free hostname, real Let's Encrypt certificate |
| Login | The username/password you set in `user-data.sh` |

## 1. Launch the instance (AWS console, ~5 minutes)

1. Open **EC2 → Launch instance** (pick a region near you, top right).
2. **Name:** `webcrawler-demo`.
3. **Application and OS image:** *Ubuntu Server 24.04 LTS*, architecture **64-bit (x86)**.
4. **Instance type:** `t3.small`.
5. **Key pair:** *Proceed without a key pair* is fine — you can still get a shell via
   **EC2 Instance Connect** in the browser. (Create one if you prefer SSH from your PC.)
6. **Network settings → Edit:**
   - Auto-assign public IP: **Enable**
   - Security group: create one, named `webcrawler-demo`, with inbound rules:
     - **HTTP (80)** from *Anywhere* — needed for the HTTPS certificate challenge and redirect
     - **HTTPS (443)** from *Anywhere*
     - **SSH (22)** from *My IP* — only for Instance Connect / SSH
7. **Configure storage:** 20 GiB, gp3.
8. **Advanced details:**
   - **Metadata version:** *V2 only (token required)*
   - **Metadata response hop limit:** **1** — containers then can't reach the instance metadata at all
     (the crawler also refuses private addresses on its own; this is a second layer).
   - **User data:** paste the whole of [`user-data.sh`](user-data.sh) — **first change `DEMO_PASSWORD`**
     (and `DEMO_USER` if you like).
9. **Launch instance.**

## 2. Wait for it (~10 minutes) and open it

The first boot installs Docker and builds the images. To find the address:

- **EC2 → Instances → your instance → Public IPv4 address**, e.g. `3.91.12.34`
- The app is at **`https://3-91-12-34.sslip.io`** (dots become dashes). Log in with your demo user.

To watch progress, connect via **Instance Connect** and run:

```bash
sudo tail -f /var/log/webcrawler-setup.log
```

```bash
cat /opt/webcrawler/SITE_URL
```

## 3. Day to day

- **Stop** the instance when you're not demoing (EC2 → Instance state → Stop): you only pay for the disk.
  **Start** it again later; the stack comes back by itself. The public IP changes after a stop, so
  look up the new address (same rule: `https://<new-ip-with-dashes>.sslip.io`). For a fixed address,
  attach an **Elastic IP** (small hourly charge while allocated).
- **Deploy a new version** after pushing to GitHub (Instance Connect shell):

  ```bash
  cd /opt/webcrawler && sudo git pull && sudo systemctl restart webcrawler
  ```

- **Logs** of a service:

  ```bash
  cd /opt/webcrawler && sudo docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f worker
  ```

- **Your own domain:** point a DNS A record at the instance, then add `SITE_ADDRESS=crawler.example.com`
  to `/opt/webcrawler/.env` and restart (`sudo systemctl restart webcrawler`).

## Troubleshooting

**"Refused to connect" in the browser, and `webcrawler.service failed` in the setup log.** Look at the cause:

```bash
sudo journalctl -u webcrawler.service --no-pager | tail -40
```

- **`no space left on device`**: the disk is the 8 GB default. In the console: instance → **Storage** tab →
  volume → **Actions → Modify volume → 20 GiB**. Then **Stop** the instance and **Start** it. Ubuntu grows the
  filesystem on boot, and the app builds and starts by itself.
- **The build is killed or hangs**: the instance is a t3.micro (1 GB). **Stop → Actions → Instance settings →
  Change instance type → t3.small → Start.**

After a stop/start the public IP changes, so use the new `https://<ip-with-dashes>.sslip.io` address.
Use the **dashed** form: the HTTPS certificate is issued for it. The dotted form also resolves, but the
certificate won't match it.

## 4. Tear down

**EC2 → Instances → Terminate.** That deletes the instance, its disk and all crawl data. If you created
an Elastic IP, release it too (EC2 → Elastic IPs), and delete the security group.

## What's protected

- **Login on everything** (Caddy basic auth over HTTPS), so strangers can't use the server to crawl.
- **No internal ports exposed**: PostgreSQL, RabbitMQ, the API and the worker publish no ports
  (`docker-compose.prod.yml`); Swagger and health endpoints aren't reachable from outside.
- **Random secrets**: database and broker passwords are generated on first boot into `/opt/webcrawler/.env`
  (mode 600); the demo password is stored only as a bcrypt hash.
- **SSRF protection**: the worker resolves every host itself and refuses private, loopback and
  link-local addresses — including the AWS metadata service `169.254.169.254` — on every request and
  redirect hop. Metadata hop limit 1 blocks containers from it as well.
