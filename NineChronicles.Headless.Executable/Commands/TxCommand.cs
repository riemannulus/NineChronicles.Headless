using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Bencodex;
using Bencodex.Types;
using Cocona;
using CsvHelper;
using Google.Protobuf.WellKnownTypes;
using Libplanet;
using Libplanet.Assets;
using Libplanet.Blocks;
using Libplanet.Consensus;
using Libplanet.Crypto;
using Libplanet.Tx;
using Nekoyume.Action;
using Nekoyume.Action.Factory;
using Nekoyume.Model;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using NineChronicles.Headless.Executable.IO;
using NCAction = Libplanet.Action.PolymorphicAction<Nekoyume.Action.ActionBase>;

namespace NineChronicles.Headless.Executable.Commands
{
    public class TxCommand : CoconaLiteConsoleAppBase
    {
        private static readonly Codec _codec = new Codec();
        private readonly IConsole _console;

        public TxCommand(IConsole console)
        {
            _console = console;
        }

        [Command(Description = "Create new transaction with given actions and dump it.")]
        public void Sign(
            [Argument("PRIVATE-KEY", Description = "A hex-encoded private key for signing.")]
            string privateKey,
            [Argument("NONCE", Description = "A nonce for new transaction.")]
            long nonce,
            [Argument("GENESIS-HASH", Description = "A hex-encoded genesis block hash.")]
            string genesisHash,
            [Argument("TIMESTAMP", Description = "A datetime for new transaction.")]
            string timestamp,
            [Option("action", new[] { 'a' }, Description = "A path of the file contained base64 encoded actions.")]
            string[] actions,
            [Option("bytes", new[] { 'b' },
                Description = "Print raw bytes instead of base64.  No trailing LF appended.")]
            bool bytes = false
        )
        {
            List<NCAction> parsedActions = actions.Select(a =>
            {
                if (File.Exists(a))
                {
                    a = File.ReadAllText(a);
                }

                var decoded = (List)_codec.Decode(Convert.FromBase64String(a));
                string type = (Text)decoded[0];
                Dictionary plainValue = (Dictionary)decoded[1];

                ActionBase action = type switch
                {
                    nameof(ActivateAccount) => new ActivateAccount(),
                    nameof(MonsterCollect) => new MonsterCollect(),
                    nameof(ClaimMonsterCollectionReward) => new ClaimMonsterCollectionReward(),
                    nameof(Stake) => new Stake(),
                    // FIXME: This `ClaimStakeReward` cases need to reduce to one case.
                    nameof(ClaimStakeReward1) => new ClaimStakeReward1(),
                    nameof(ClaimStakeReward) => new ClaimStakeReward(),
                    nameof(ClaimStakeReward3) => new ClaimStakeReward3(),
                    nameof(TransferAsset) => new TransferAsset(),
                    nameof(MigrateMonsterCollection) => new MigrateMonsterCollection(),
                    _ => throw new CommandExitedException($"Unsupported action type was passed '{type}'", 128)
                };
                action.LoadPlainValue(plainValue);

                return (NCAction)action;
            }).ToList();

            Transaction<NCAction> tx = Transaction<NCAction>.Create(
                nonce: nonce,
                privateKey: new PrivateKey(ByteUtil.ParseHex(privateKey)),
                genesisHash: BlockHash.FromString(genesisHash),
                timestamp: DateTimeOffset.Parse(timestamp),
                customActions: parsedActions
            );
            byte[] raw = tx.Serialize(true);

            if (bytes)
            {
                _console.Out.WriteLine(raw);
            }
            else
            {
                _console.Out.WriteLine(Convert.ToBase64String(raw));
            }
        }

        public void TransferAsset(
            [Argument("SENDER", Description = "An address of sender.")]
            string sender,
            [Argument("RECIPIENT", Description = "An address of recipient.")]
            string recipient,
            [Argument("AMOUNT", Description = "An amount of gold to transfer.")]
            int goldAmount,
            [Argument("GENESIS-BLOCK", Description = "A genesis block containing InitializeStates.")]
            string genesisBlock
        )
        {
            byte[] genesisBytes = File.ReadAllBytes(genesisBlock);
            var genesisDict = (Bencodex.Types.Dictionary)_codec.Decode(genesisBytes);
            IReadOnlyList<Transaction<NCAction>> genesisTxs =
                BlockMarshaler.UnmarshalBlockTransactions<NCAction>(genesisDict);
            var initStates = (InitializeStates)genesisTxs.Single().CustomActions!.Single().InnerAction;
            Currency currency = new GoldCurrencyState(initStates.GoldCurrency).Currency;

            var action = new TransferAsset(
                new Address(sender),
                new Address(recipient),
                currency * goldAmount
            );

            var bencoded = new List(
                (Text)nameof(TransferAsset),
                action.PlainValue
            );

            byte[] raw = _codec.Encode(bencoded);
            _console.Out.Write(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create PatchTable action and dump it.")]
        public void PatchTable(
            [Argument("TABLE-PATH", Description = "A table file path for patch.")]
            string tablePath
        )
        {
            var tableName = Path.GetFileName(tablePath);
            if (tableName.EndsWith(".csv"))
            {
                tableName = tableName.Split(".csv")[0];
            }

            _console.Error.Write("----------------\n");
            _console.Error.Write(tableName);
            _console.Error.Write("\n----------------\n");
            var tableCsv = File.ReadAllText(tablePath);
            _console.Error.Write(tableCsv);

            var type = typeof(ISheet).Assembly
                .GetTypes()
                .First(type => type.Namespace is { } @namespace &&
                               @namespace.StartsWith($"{nameof(Nekoyume)}.{nameof(Nekoyume.TableData)}") &&
                               !type.IsAbstract &&
                               typeof(ISheet).IsAssignableFrom(type) &&
                               type.Name == tableName);
            var sheet = (ISheet)Activator.CreateInstance(type)!;
            sheet.Set(tableCsv);

            var action = new PatchTableSheet
            {
                TableName = tableName,
                TableCsv = tableCsv
            };

            var bencoded = new List(
                (Text)nameof(PatchTableSheet),
                action.PlainValue
            );

            byte[] raw = _codec.Encode(bencoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create MigrationLegacyShop action and dump it.")]
        public void MigrationLegacyShop()
        {
            var action = new MigrationLegacyShop();

            var bencoded = new List(
                (Text)nameof(Nekoyume.Action.MigrationLegacyShop),
                action.PlainValue
            );

            byte[] raw = _codec.Encode(bencoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create MigrationActivatedAccountsState action and dump it.")]
        public void MigrationActivatedAccountsState()
        {
            var action = new MigrationActivatedAccountsState();
            var bencoded = new List(
                (Text)nameof(Nekoyume.Action.MigrationActivatedAccountsState),
                action.PlainValue
            );

            byte[] raw = _codec.Encode(bencoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create MigrationAvatarState action and dump it.")]
        public void MigrationAvatarState(
            [Argument("directory-path", Description = "path of the directory contained hex-encoded avatar states.")]
            string directoryPath,
            [Argument("output-path", Description = "path of the output file dumped action.")]
            string outputPath
        )
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var avatarStates = files.Select(a =>
            {
                var raw = File.ReadAllText(a);
                return (Dictionary)_codec.Decode(ByteUtil.ParseHex(raw));
            }).ToList();
            var action = new MigrationAvatarState()
            {
                avatarStates = avatarStates
            };

            var encoded = new List(
                (Text)nameof(Nekoyume.Action.MigrationAvatarState),
                action.PlainValue
            );

            byte[] raw = _codec.Encode(encoded);
            File.WriteAllText(outputPath, ByteUtil.Hex(raw));
        }

        [Command(Description = "Create AddRedeemCode action and dump it.")]
        public void AddRedeemCode(
            [Argument("TABLE-PATH", Description = "A table file path for RedeemCodeListSheet")]
            string tablePath
        )
        {
            var tableCsv = File.ReadAllText(tablePath);
            var action = new AddRedeemCode
            {
                redeemCsv = tableCsv
            };
            var encoded = new List(
                (Text)nameof(Nekoyume.Action.AddRedeemCode),
                action.PlainValue
            );
            byte[] raw = _codec.Encode(encoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create CreatePendingActivations action and dump it.")]
        public void CreatePendingActivations(
            [Argument("CSV-PATH", Description = "A csv file path for CreatePendingActivations")]
            string csvPath
        )
        {
            var RecordType = new
            {
                EncodedActivationKey = string.Empty,
                NonceHex = string.Empty,
            };
            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var activations =
                csv.GetRecords(RecordType)
                    .Select(r => new PendingActivationState(
                        ByteUtil.ParseHex(r.NonceHex),
                        ActivationKey.Decode(r.EncodedActivationKey).PrivateKey.PublicKey)
                    )
                    .ToList();
            var action = new CreatePendingActivations(activations);
            var encoded = new List(
                new IValue[]
                {
                    (Text)nameof(Nekoyume.Action.CreatePendingActivations),
                    action.PlainValue
                }
            );
            byte[] raw = _codec.Encode(encoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create RenewAdminState action and dump it.")]
        public void RenewAdminState(
            [Argument("NEW-VALID-UNTIL")] long newValidUntil
        )
        {
            RenewAdminState action = new RenewAdminState(newValidUntil);
            var encoded = new List(
                (Text)nameof(Nekoyume.Action.RenewAdminState),
                action.PlainValue
            );
            byte[] raw = _codec.Encode(encoded);
            _console.Out.WriteLine(ByteUtil.Hex(raw));
        }

        [Command(Description = "Create ActvationKey-nonce pairs and dump them as csv")]
        public void CreateActivationKeys(
            [Argument("COUNT", Description = "An amount of pairs")]
            int count
        )
        {
            var rng = new Random();
            var nonce = new byte[4];
            _console.Out.WriteLine("EncodedActivationKey,NonceHex");
            foreach (int i in Enumerable.Range(0, count))
            {
                PrivateKey key;
                while (true)
                {
                    key = new PrivateKey();
                    if (key.ToByteArray().Length == 32)
                    {
                        break;
                    }
                }

                rng.NextBytes(nonce);
                var (ak, _) = ActivationKey.Create(key, nonce);
                _console.Out.WriteLine($"{ak.Encode()},{ByteUtil.Hex(nonce)}");
            }
        }
        
        [Command(Description = "Create ActvationKey-nonce pairs and dump them as csv")]
        public void CreateValidatorSetAppendTx()
        {
            var validatorCandidates = new[]
            {
                "0260972c353ba9b1e630d7b488ef0e9250a86286fbc8541e1bcca82f1a50cf8012",
                "029a46496641787a06db32d83dfea3b50ebb681fae9bbf4e60b41ee91d4024965b",
                "021ebc027706c9b7fdb03bc212657241197c4f7d4f122cbb66f347bee3bfd39551",
                "03310066ad080de4bea6042163cade4ab777a1ccb45abfada0973352b34ca0b497",
            };
            var startNonce = 350;
            PublicKey signer = new PublicKey(
                ByteUtil.ParseHex("0326e7f518eadfb1addc320755eeb78e385cf4b9d56986677a092a708c86990ae1"));
            BlockHash genesisHash = BlockHash.FromString("4582250d0da33b06779a8475d283d5dd210c683b9b999d74d03fac4f58fa6bce");
            DateTimeOffset txTimestamp = new DateTime(2023, 4, 1);
            
            var createUnsignedTxFunc = new Func<ValidatorSetOperate, int, Transaction<NCAction>>((operate, i) =>
                Transaction<NCAction>.CreateUnsigned(
                    startNonce + i,
                    signer,
                    genesisHash,
                    new List<NCAction>
                    {
                        new(operate)
                    },
                    timestamp: txTimestamp));

            var validatorSetOperateTxs = validatorCandidates
                .Select(candidateString =>
                    new Validator(new PublicKey(ByteUtil.ParseHex(candidateString)), BigInteger.One))
                .Select(ValidatorSetOperate.Append)
                .Select(createUnsignedTxFunc);
            foreach (var transaction in validatorSetOperateTxs)
            {
                _console.Out.WriteLine(ByteUtil.Hex(transaction.Serialize(false)));
            }
        }

        [Command(Description = "Create ActvationKey-nonce pairs and dump them as csv")]
        public void Validate()
        {
            var txString = "64313a616c6475373a747970655f69647531363a6f705f76616c696461746f725f73657475363a76616c7565736475323a6f7075363a417070656e6475373a6f706572616e6464313a5033333a03310066ad080de4bea6042163cade4ab777a1ccb45abfada0973352b34ca0b497313a7069316565656565313a6733323a4582250d0da33b06779a8475d283d5dd210c683b9b999d74d03fac4f58fa6bce313a6e6933353365313a7036353a0426e7f518eadfb1addc320755eeb78e385cf4b9d56986677a092a708c86990ae18937bb6413076cbcb641e6460c3fd52b67262b08957e65790624412300ffbb73313a7332303aa1ef9701f151244f9aa7131639990c4664d2aeef313a747532373a323032332d30332d33315431353a30303a30302e3030303030305a313a756c6565";
            byte[] txHex = ByteUtil.ParseHex(txString);
            Transaction<NCAction> tx = Transaction<NCAction>.Deserialize(txHex, validate: false);
            if (tx.CustomActions == null)
            {
                return;
            }
            foreach (var customAction in tx.CustomActions)
            {
                if(customAction.InnerAction is ValidatorSetOperate validatorSetOperate)
                {
                    _console.Out.WriteLine(tx.Signer);
                }
            }
        }
    }
}
