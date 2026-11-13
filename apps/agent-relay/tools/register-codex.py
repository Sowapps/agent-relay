"""Compatible registration command; behavior is owned by codex_registration."""

from codex_registration import (
    DEVELOPMENT_SERVER, LEGACY_STABLE_SERVER, SHARED_SERVER,
    main, replace_if_unchanged, replace_servers, toml_value, update,
)


if __name__ == '__main__':
    main()
