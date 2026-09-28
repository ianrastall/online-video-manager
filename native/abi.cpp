#include "internal.hpp"
#include "ovm.h"
#include <cstdlib>
#include <cstring>

namespace
{
std::mutex handles_mutex;
std::map<uint64_t, std::shared_ptr<ovm::Engine>> handles;
std::atomic_uint64_t next_handle{1};
char *response(const ovm::json &data) noexcept
{
    try
    {
        const auto text = data.dump(-1, ' ', false, ovm::json::error_handler_t::replace);
        auto p = static_cast<char *>(std::malloc(text.size() + 1));
        if (p)
            std::memcpy(p, text.c_str(), text.size() + 1);
        return p;
    }
    catch (...)
    {
        return nullptr;
    }
}
char *failure(const char *error) noexcept
{
    try
    {
        return response({{"error", error}});
    }
    catch (...)
    {
        return nullptr;
    }
}
} // namespace
extern "C"
{
    int32_t OVM_CALL ovm_abi_version(void)
    {
        return 2;
    }
    char *OVM_CALL ovm_open(const char *home)
    {
        try
        {
            auto engine = std::make_shared<ovm::Engine>(home && *home ? ovm::path(home) : ovm::default_home());
            auto id = next_handle++;
            auto result = response({{"handle", id}});
            if (!result)
                return nullptr;
            try
            {
                std::lock_guard lock(handles_mutex);
                handles.emplace(id, std::move(engine));
            }
            catch (...)
            {
                std::free(result);
                throw;
            }
            return result;
        }
        catch (const std::exception &ex)
        {
            return failure(ex.what());
        }
        catch (...)
        {
            return failure("Unexpected native error.");
        }
    }
    char *OVM_CALL ovm_execute(uint64_t id, const char *request)
    {
        try
        {
            std::shared_ptr<ovm::Engine> engine;
            {
                std::lock_guard lock(handles_mutex);
                auto i = handles.find(id);
                if (i == handles.end())
                    throw std::runtime_error("Invalid engine handle.");
                engine = i->second;
            }
            if (!request || strnlen_s(request, 32 * 1024 * 1024) >= 32 * 1024 * 1024)
                throw std::runtime_error("Missing or oversized request.");
            return response(engine->execute(ovm::json::parse(request)));
        }
        catch (const std::exception &ex)
        {
            return failure(ex.what());
        }
        catch (...)
        {
            return failure("Unexpected native error.");
        }
    }
    void OVM_CALL ovm_close(uint64_t id)
    {
        try
        {
            std::shared_ptr<ovm::Engine> engine;
            {
                std::lock_guard lock(handles_mutex);
                auto i = handles.find(id);
                if (i == handles.end())
                    return;
                engine = std::move(i->second);
                handles.erase(i);
            }
            engine.reset();
        }
        catch (...)
        {
        }
    }
    void OVM_CALL ovm_free(char *data)
    {
        std::free(data);
    }
}
